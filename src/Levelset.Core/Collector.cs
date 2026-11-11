using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Levelset;

/// <summary>Gathers everything the checks need, once. Only GET and the MCP/x402 conversations leave the engine.</summary>
public sealed partial class Collector(IFetcher fetcher, IBrowserProbe? browser)
{
    private static readonly string[] CardPaths = ["/.well-known/mcp/server-card.json", "/.well-known/mcp.json"];
    private static readonly string[] SupportedProtocols = ["2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25"];
    private static readonly string[] TestnetPrefixes = ["eip155:84532", "eip155:11155111", "solana:EtWTRABZaYq6iMfeYKouRu166VU2xqa1"];
    public static readonly Uri DefaultTestnetFacilitator = new("https://x402.org/facilitator");

    public async Task<Snapshot> CollectAsync(Uri url, ScanOptions options, CancellationToken ct)
    {
        var s = new Snapshot { Url = url, Options = options };
        var home = Task.Run(async () => s.Home = await fetcher.GetAsync(url, ct, Accept("text/html,*/*")), ct);
        var md = Task.Run(async () => s.Markdown = await fetcher.GetAsync(url, ct, Accept("text/markdown")), ct);
        var robots = Task.Run(async () =>
        {
            s.Robots = await fetcher.GetAsync(s.Resolve("/robots.txt"), ct);
            var sitemap = SitemapRefs(s.Robots).FirstOrDefault();
            if (sitemap is not null && Uri.TryCreate(s.Url, sitemap, out var su))
                s.Sitemap = await fetcher.GetAsync(su, ct);
        }, ct);
        var llms = Task.Run(async () =>
        {
            s.Llms = await fetcher.GetAsync(s.Resolve("/llms.txt"), ct);
            if (s.Llms.Ok) await ProbeLlmsLinksAsync(s, ct);
        }, ct);
        var misc = Task.Run(async () =>
        {
            s.LlmsFull = await fetcher.GetAsync(s.Resolve("/llms-full.txt"), ct);
            s.ApiCatalog = await fetcher.GetAsync(s.Resolve("/.well-known/api-catalog"), ct);
            var card = await fetcher.GetAsync(s.Resolve("/.well-known/agent-card.json"), ct);
            s.AgentCard = card.Ok ? card : await fetcher.GetAsync(s.Resolve("/agents.json"), ct);
        }, ct);
        var browserTask = Task.Run(async () =>
        {
            if (browser is null || options.SkipBrowser) return;
            s.Browser = await browser.ProbeAsync(url, options.AllowPrivate, ct);
        }, ct);

        await Task.WhenAll(home, md, robots, llms, misc, browserTask);
        s.Mcp = await CollectMcpAsync(s, ct);
        s.X402 = await CollectX402Async(s, ct);
        return s;
    }

    private static Dictionary<string, string> Accept(string value) => new() { ["Accept"] = value };

    public static IEnumerable<string> SitemapRefs(FetchResult? robots) =>
        robots is { Ok: true }
            ? robots.Body.Split('\n').Select(l => l.Trim())
                .Where(l => l.StartsWith("sitemap:", StringComparison.OrdinalIgnoreCase))
                .Select(l => l[8..].Trim())
            : [];

    private async Task ProbeLlmsLinksAsync(Snapshot s, CancellationToken ct)
    {
        var links = LlmsLink().Matches(s.Llms!.Body).Select(m => m.Groups[1].Value).Distinct().Take(25).ToList();
        using var gate = new SemaphoreSlim(6);
        var results = await Task.WhenAll(links.Select(async link =>
        {
            if (!Uri.TryCreate(s.Url, link, out var u) || !UrlGuard.IsWebUrl(u))
                return (link, FetchResult.Failed(s.Url, "not a web link"));
            await gate.WaitAsync(ct);
            try { return (link, await fetcher.GetAsync(u, ct)); }
            finally { gate.Release(); }
        }));
        s.LlmsLinks.AddRange(results);
    }

    // ---- MCP ----

    private async Task<McpResult?> CollectMcpAsync(Snapshot s, CancellationToken ct)
    {
        Uri? endpoint = s.Options.McpEndpoint;
        string? via = endpoint is null ? null : "supplied";
        Uri? cardUrl = null;
        JsonNode? card = null;
        foreach (var path in CardPaths)
        {
            var r = await fetcher.GetAsync(s.Resolve(path), ct);
            if (!r.Ok || TryParse(r.Body) is not { } parsed) continue;
            cardUrl = s.Resolve(path);
            card = parsed;
            if (endpoint is null && CardEndpoint(parsed) is { } e && Uri.TryCreate(cardUrl, e, out var eu))
            {
                endpoint = eu;
                via = "server card";
            }

            break;
        }

        if (endpoint is null && s.Home is { } home)
        {
            var link = LinkRel(home.Header("link"), "mcp") ?? HtmlLinkRel(home.Body, "mcp");
            if (link is not null && Uri.TryCreate(s.Url, link, out var lu))
            {
                endpoint = lu;
                via = "link";
            }
        }

        if (endpoint is null) return null;

        var json = new Dictionary<string, string>
        {
            ["Accept"] = "application/json, text/event-stream",
        };
        var init = await fetcher.SendAsync(HttpMethod.Post, endpoint, json, InitializeBody(), ct);
        var unauthorized = init.Status == 401;
        int? oauth = null;
        if (unauthorized)
            oauth = (await fetcher.GetAsync(s.Resolve("/.well-known/oauth-protected-resource"), ct)).Status;

        var initResult = Rpc(init)?["result"];
        var version = initResult?["protocolVersion"]?.ToString();
        FetchResult? list = null;
        List<ToolDescriptor>? tools = null;
        if (version is not null)
        {
            var headers = new Dictionary<string, string>(json) { ["MCP-Protocol-Version"] = version };
            if (init.Header("mcp-session-id") is { } sid) headers["Mcp-Session-Id"] = sid;
            await fetcher.SendAsync(HttpMethod.Post, endpoint, headers,
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""", ct);
            list = await fetcher.SendAsync(HttpMethod.Post, endpoint, headers,
                """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", ct);
            tools = ParseTools(Rpc(list)?["result"]?["tools"] as JsonArray);
        }

        var foreignHeaders = new Dictionary<string, string>(json) { ["Origin"] = "https://levelset-origin-test.invalid" };
        var foreign = await fetcher.SendAsync(HttpMethod.Post, endpoint, foreignHeaders, InitializeBody(), ct);

        return new McpResult(endpoint, via, cardUrl, card, init, version, list, tools, foreign.Status, unauthorized, oauth);
    }

    private static string InitializeBody() =>
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"levelset","version":"1"}}}""";

    public static bool IsSupportedProtocol(string? v) => v is not null && SupportedProtocols.Contains(v);

    private static JsonNode? Rpc(FetchResult r)
    {
        if (r.Error is not null || r.Status is < 200 or >= 300) return null;
        var body = r.Body;
        if (r.ContentType.Contains("text/event-stream"))
        {
            var data = body.Split('\n').FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal));
            if (data is null) return null;
            body = data[5..].Trim();
        }

        return TryParse(body);
    }

    public static JsonNode? TryParse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    private static string? CardEndpoint(JsonNode card)
    {
        foreach (var path in new[]
                 {
                     "remotes/0/url", "endpoint", "url", "transport/endpoint", "transport/url", "endpoints/streamableHttp",
                 })
        {
            JsonNode? node = card;
            foreach (var part in path.Split('/'))
                node = int.TryParse(part, out var i) ? (node as JsonArray)?.ElementAtOrDefault(i) : node?[part];
            if (node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0) return s;
        }

        return null;
    }

    private static List<ToolDescriptor> ParseTools(JsonArray? arr) =>
        arr is null
            ? []
            : arr.OfType<JsonObject>().Select(o => new ToolDescriptor("mcp", o["name"]?.ToString(),
                o["description"]?.ToString(), o["inputSchema"], o["outputSchema"], o["annotations"] as JsonObject)).ToList();

    // ---- x402 ----

    private async Task<X402Result?> CollectX402Async(Snapshot s, CancellationToken ct)
    {
        var urls = new List<Uri> { s.Url };
        urls.AddRange(s.Options.PaidPaths.Select(p => new Uri(s.Url, p)));
        var challenges = new List<X402Challenge>();
        foreach (var u in urls.Distinct())
        {
            var r = u == s.Url && s.Home is not null ? s.Home : await fetcher.GetAsync(u, ct);
            if (r.Status != 402) continue;
            var (body, origin) = ParseChallenge(r);
            challenges.Add(new X402Challenge(u, body ?? new JsonObject(), origin));
        }

        var hints = s.Mcp?.ToolsList is { } tl
            ? (Rpc(tl)?["result"]?["tools"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(t => t["_meta"] is JsonObject m && m.Any(kv =>
                    kv.Key.Contains("x402", StringComparison.OrdinalIgnoreCase)
                    || kv.Key.Contains("price", StringComparison.OrdinalIgnoreCase)))
                .Select(t => t["name"]?.ToString() ?? "(unnamed)").ToList()
            : [];
        if (challenges.Count == 0 && hints.Count == 0) return null;

        var facilitator = s.Options.Facilitator;
        if (facilitator is null && challenges.Any(c => Networks(c.Body).Any(IsTestnet)))
            facilitator = DefaultTestnetFacilitator;
        FetchResult? supported = null;
        if (facilitator is not null && challenges.Count > 0)
            supported = await fetcher.GetAsync(new Uri(facilitator.ToString().TrimEnd('/') + "/supported"), ct);
        return new X402Result(challenges, hints, facilitator, supported);
    }

    private static (JsonNode? Body, string Origin) ParseChallenge(FetchResult r)
    {
        if (r.Header("payment-required") is { } h)
        {
            try
            {
                return (TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(h))), "header");
            }
            catch (FormatException) { /* fall through to the body */ }
        }

        return (TryParse(r.Body), "body");
    }

    public static IEnumerable<string> Networks(JsonNode body) =>
        (body["accepts"] as JsonArray ?? []).Select(a => a?["network"]?.ToString() ?? "");

    public static bool IsTestnet(string network) => TestnetPrefixes.Any(p => network.StartsWith(p, StringComparison.Ordinal));

    public static string? LinkRel(string? header, string rel)
    {
        if (header is null) return null;
        var m = Regex.Match(header, $"<([^>]+)>\\s*;[^,]*rel=\"?{Regex.Escape(rel)}\"?", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string? HtmlLinkRel(string html, string rel)
    {
        foreach (Match m in HtmlLink().Matches(html))
        {
            var tag = m.Value;
            if (!Regex.IsMatch(tag, $"rel=[\"']{Regex.Escape(rel)}[\"']", RegexOptions.IgnoreCase)) continue;
            var href = Regex.Match(tag, "href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (href.Success) return href.Groups[1].Value;
        }

        return null;
    }

    [GeneratedRegex(@"\]\(([^)\s]+)")]
    private static partial Regex LlmsLink();

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLink();
}
