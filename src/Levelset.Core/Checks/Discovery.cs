using System.Text.RegularExpressions;

namespace Levelset;

public static partial class Catalog
{
    private static readonly string[] AiBots = ["GPTBot", "ClaudeBot", "Google-Extended", "PerplexityBot"];

    private static IEnumerable<Check> DiscoveryChecks()
    {
        const Category D = Category.Discovery;
        yield return Def("DISC-001", D, Severity.Warning, CheckStatus.DeFacto, "llms.txt exists and is well formed",
            LlmsSpec,
            "Serve /llms.txt as text/markdown or text/plain: an H1 title, a blockquote summary, then link sections that all answer 2xx. It helps agents find your content; it does not affect search ranking.",
            run: s =>
            {
                var l = s.Llms;
                if (l is null || !l.Ok) return One("/llms.txt", "no /llms.txt (status " + (l?.Status ?? 0) + ")");
                var problems = new List<Problem>();
                if (!(l.ContentType.Contains("text/markdown") || l.ContentType.Contains("text/plain")))
                    problems.Add(new("/llms.txt", $"content type is \"{l.ContentType}\", expected text/markdown or text/plain"));
                else
                {
                    if (!Regex.IsMatch(l.Body, @"^# \S", RegexOptions.Multiline)) problems.Add(new("/llms.txt", "no H1 title"));
                    if (!Regex.IsMatch(l.Body, @"^> \S", RegexOptions.Multiline)) problems.Add(new("/llms.txt", "no blockquote summary"));
                    if (s.LlmsLinks.Count == 0) problems.Add(new("/llms.txt", "no links to pages"));
                }

                problems.AddRange(s.LlmsLinks.Where(x => !x.Result.Ok).Select(x => new Problem("/llms.txt", $"link {x.Link} answers {(x.Result.Status == 0 ? x.Result.Error : x.Result.Status)}")));
                return problems;
            },
            snippet: "# Site name\n\n> One sentence on what the site is.\n\n## Docs\n\n- [Getting started](/docs/start): what it covers");
        yield return Def("DISC-002", D, Severity.Info, CheckStatus.DeFacto, "llms-full.txt present", LlmsSpec,
            "Optional: /llms-full.txt holds the full text of your docs in one file.",
            s => s.LlmsFull is { Ok: true } l && l.ContentType.Contains("text/") ? One("/llms-full.txt", "found") : Pass);
        yield return Def("DISC-003", D, Severity.Info, CheckStatus.Vendor, "llms.txt advertised in a header or link tag", LlmsSpec,
            "Optional: add Link: </llms.txt>; rel=\"llms-txt\" or a <link rel=\"llms-txt\"> tag.",
            s => s.Home is { } h && (Collector.LinkRel(h.Header("link"), "llms-txt") is not null || Collector.HtmlLinkRel(h.Body, "llms-txt") is not null)
                ? One("/", "llms-txt link found") : Pass);
        yield return Def("DISC-010", D, Severity.Warning, CheckStatus.Standard, "robots.txt present and deliberate about AI agents",
            "https://www.rfc-editor.org/rfc/rfc9309",
            "Serve /robots.txt. Do not disallow everything for *; name the AI user agents you want to allow or block (GPTBot, ClaudeBot, Google-Extended, PerplexityBot), or keep a * group that covers them.",
            s =>
            {
                var r = s.Robots;
                if (r is null || !r.Ok || r.ContentType.Contains("html")) return One("/robots.txt", "no robots.txt");
                var groups = RobotsGroups(r.Body);
                if (groups.TryGetValue("*", out var star) && star.Contains("/"))
                    return One("/robots.txt", "User-agent: * disallows the whole site");
                if (groups.ContainsKey("*")) return Pass;
                var missing = AiBots.Where(b => !groups.Keys.Any(k => k.Equals(b, StringComparison.OrdinalIgnoreCase))).ToList();
                return missing.Count == 0 ? Pass : One("/robots.txt", "no * group and no rule for " + string.Join(", ", missing));
            },
            snippet: "User-agent: *\nAllow: /\n\nSitemap: https://example.com/sitemap.xml");
        yield return Def("DISC-011", D, Severity.Info, CheckStatus.Draft, "Content Signals in robots.txt", "https://contentsignals.org/",
            "Optional: add a Content-Signal line to say how your content may be used (search, ai-input, ai-train).",
            s => s.Robots is { Ok: true } r && r.Body.Contains("Content-Signal:", StringComparison.OrdinalIgnoreCase)
                ? One("/robots.txt", "Content-Signal found") : Pass);
        yield return Def("DISC-020", D, Severity.Warning, CheckStatus.DeFacto, "Markdown negotiation",
            "https://developers.cloudflare.com/fundamentals/reference/markdown-for-agents/",
            "Answer Accept: text/markdown with a markdown version of the page, Content-Type: text/markdown and Vary: Accept.",
            s =>
            {
                var m = s.Markdown;
                var problems = new List<Problem>();
                if (m is null || !m.Ok || !m.ContentType.Contains("text/markdown"))
                    return One("/", "Accept: text/markdown does not return text/markdown");
                if (!(m.Header("vary")?.Contains("accept", StringComparison.OrdinalIgnoreCase) ?? false))
                    problems.Add(new("/", "markdown response lacks Vary: Accept"));
                return problems;
            },
            dotnet: "app.Use(async (ctx, next) => { ctx.Response.Headers.Vary = \"Accept\"; await next(); });");
        yield return Def("DISC-021", D, Severity.Warning, CheckStatus.Standard, "Sitemap referenced and reachable",
            "https://www.sitemaps.org/protocol.html",
            "Add a Sitemap: line to robots.txt pointing at a sitemap that answers 2xx.",
            s =>
            {
                if (Collector.SitemapRefs(s.Robots).FirstOrDefault() is null) return One("/robots.txt", "no Sitemap: line");
                return s.Sitemap is { Ok: true } ? Pass : One("/robots.txt", "the referenced sitemap does not answer 2xx");
            });
        yield return Def("DISC-030", D, Severity.Info, CheckStatus.Standard, "API catalog", "https://www.rfc-editor.org/rfc/rfc9727",
            "Optional: when you advertise an API, publish /.well-known/api-catalog (RFC 9727).",
            s => s.ApiCatalog is { Ok: true } a && !a.ContentType.Contains("html") ? One("/.well-known/api-catalog", "found") : Pass);
        yield return Def("DISC-031", D, Severity.Info, CheckStatus.Vendor, "A2A agent card or agents.json", "https://a2a-protocol.org/",
            "Detected only; not scored.",
            s => s.AgentCard is { Ok: true } a && !a.ContentType.Contains("html") ? One("/", "agent card found") : Pass);
        yield return Def("DISC-040", D, Severity.Warning, CheckStatus.Standard, "Main content is in the server-rendered HTML", "https://developer.mozilla.org/docs/Glossary/SSR",
            "Render the main content on the server so a fetch without JavaScript sees at least 60% of what a browser sees.",
            s =>
            {
                if (s.Browser is not { Error: null } b || s.Home is not { Ok: true } h || b.RenderedTextLength < 200) return null;
                var served = VisibleText(h.Body).Length;
                var ratio = (double)served / b.RenderedTextLength;
                return ratio >= 0.6 ? Pass : One("/", $"the HTML holds {ratio:P0} of the text a browser shows");
            },
            dotnet: "Prefer Razor Pages, MVC views or Blazor static SSR over a client-only SPA for content pages.");
        yield return Def("DISC-041", D, Severity.Warning, CheckStatus.Standard, "Structured data (JSON-LD)", "https://schema.org/docs/gs.html",
            "Add a <script type=\"application/ld+json\"> block describing the page's main entity.",
            s => s.Home is { Ok: true } h && h.Body.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase) ? Pass : One("/", "no JSON-LD"),
            snippet: "<script type=\"application/ld+json\">{\"@context\":\"https://schema.org\",\"@type\":\"WebSite\",\"name\":\"Site\",\"url\":\"https://example.com/\"}</script>");
    }

    public static Dictionary<string, List<string>> RobotsGroups(string body)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var i = line.IndexOf(':');
            if (i < 0) continue;
            var key = line[..i].Trim().ToLowerInvariant();
            var value = line[(i + 1)..].Trim();
            if (key == "user-agent")
            {
                if (!groups.TryGetValue(value, out current)) groups[value] = current = [];
            }
            else
            {
                if (key == "disallow" && value == "/" && current is not null) current.Add("/");
            }
        }

        return groups;
    }

    public static string VisibleText(string html)
    {
        var t = Regex.Replace(html, @"<(script|style|noscript)\b[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]+>", " ");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
}
