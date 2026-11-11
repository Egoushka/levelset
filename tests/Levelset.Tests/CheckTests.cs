using System.Text;
using System.Text.Json.Nodes;

namespace Levelset.Tests;

public class CheckTests
{
    private static IEnumerable<string> Ids(Report r) => r.Findings.Select(f => f.Id);

    [Fact]
    public void PlainSiteGetsWarningsButNoErrors()
    {
        var r = Fx.Eval(Fx.Snap());
        Assert.DoesNotContain(r.Findings, f => f.Severity == "error");
        Assert.Contains("DISC-001", Ids(r));
        Assert.Contains("DISC-010", Ids(r));
        Assert.True(r.Score < 100);
        Assert.Null(r.Categories["webmcp"]);
        Assert.Null(r.Categories["x402"]);
    }

    [Fact]
    public void CatalogIdsAreUniqueAndEveryCheckHasAFixAndSpec()
    {
        Assert.Equal(Catalog.All.Count, Catalog.All.Select(c => c.Info.Id).Distinct().Count());
        Assert.All(Catalog.All, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Info.FixText));
            Assert.StartsWith("https://", c.Info.Spec);
        });
        Assert.All(Catalog.All.Where(c => c.Info.Status is CheckStatus.Draft or CheckStatus.Vendor), c =>
            Assert.True(Scorer.Loss(c.Info) <= Scorer.DraftCap));
    }

    [Fact]
    public void WellFormedLlmsTxtPasses()
    {
        var s = Fx.Snap(x =>
        {
            x.Llms = FakeFetcher.Resp("https://example.test/llms.txt", 200, "# Site\n\n> What it is.\n\n- [Docs](/docs)", "text/markdown");
            x.LlmsLinks.Add(("/docs", FakeFetcher.Resp("https://example.test/docs", 200, "ok")));
        });
        Assert.Contains("DISC-001", Fx.Eval(s).Passed);
    }

    [Fact]
    public void BrokenLlmsLinkIsReported()
    {
        var s = Fx.Snap(x =>
        {
            x.Llms = FakeFetcher.Resp("https://example.test/llms.txt", 200, "# Site\n\n> What it is.\n\n- [Docs](/docs)", "text/markdown");
            x.LlmsLinks.Add(("/docs", FakeFetcher.Resp("https://example.test/docs", 404, "no")));
        });
        Assert.Contains(Fx.Eval(s).Findings, f => f.Id == "DISC-001" && f.Message.Contains("/docs"));
    }

    [Fact]
    public void RobotsThatBlocksEverythingIsFlagged()
    {
        var s = Fx.Snap(x => x.Robots = FakeFetcher.Resp("https://example.test/robots.txt", 200, "User-agent: *\nDisallow: /"));
        Assert.Contains(Fx.Eval(s).Findings, f => f.Id == "DISC-010" && f.Message.Contains("whole site"));
    }

    [Fact]
    public void MarkdownNegotiationNeedsVary()
    {
        var s = Fx.Snap(x => x.Markdown = FakeFetcher.Resp("https://example.test/", 200, "# Hi", "text/markdown"));
        Assert.Contains(Fx.Eval(s).Findings, f => f.Id == "DISC-020" && f.Message.Contains("Vary"));
    }

    [Fact]
    public void HttpSiteIsAnError()
    {
        var s = Fx.Snap();
        var http = new Snapshot { Url = new Uri("http://example.test/"), Options = new ScanOptions(), Home = FakeFetcher.Resp("http://example.test/", 200, "x", "text/html") };
        Assert.Contains(Fx.Eval(http).Findings, f => f.Id == "HYG-001" && f.Severity == "error");
        Assert.DoesNotContain(Ids(Fx.Eval(s)), id => id == "HYG-001");
    }

    [Fact]
    public void WebMcpToolProblemsAreFound()
    {
        var s = Fx.Snap(x => x.Browser = Fx.Page(
            Fx.Tool("delete_note", "Deletes a note from the notebook for good."),
            Fx.Tool("bad name!", "x"),
            Fx.Tool("fetch_data", "Fetch the data the page shows to the user.",
                JsonNode.Parse("""{"type":"object","properties":{"a":{"type":"string"}},"required":["b"]}""")),
            Fx.Tool("get_status", "Reads the current status.", annotations: """{"readOnlyHint":true}"""),
            Fx.Tool("update_item", "Updates an item in the store.", annotations: """{"readOnlyHint":true}""")));
        var f = Fx.Eval(s).Findings;
        Assert.Contains(f, x => x.Id == "WM-020" && x.Target == "tool:delete_note" && x.Severity == "error");
        Assert.Contains(f, x => x.Id == "WM-010" && x.Target == "tool:bad name!");
        Assert.Contains(f, x => x.Id == "WM-011" && x.Message.Contains("\"b\""));
        Assert.Contains(f, x => x.Id == "WM-013" && x.Target == "tool:fetch_data");
        Assert.Contains(f, x => x.Id == "WM-022" && x.Target == "tool:update_item");
        Assert.DoesNotContain(f, x => x.Id == "WM-020" && x.Target == "tool:get_status");
    }

    [Fact]
    public void ConsequentialHintSatisfiesWm020()
    {
        var s = Fx.Snap(x => x.Browser = Fx.Page(Fx.Tool("delete_note", "Deletes a note from the notebook for good.", annotations: """{"consequentialHint":true}""")));
        Assert.DoesNotContain(Ids(Fx.Eval(s)), id => id == "WM-020");
    }

    [Fact]
    public void AutoSubmittingPostFormIsAnError()
    {
        var s = Fx.Snap(x => x.Browser = Fx.Page(Fx.Tool("order", "Place an order for the items in the cart.", source: "declarative", autoSubmit: true, method: "post")));
        Assert.Contains(Fx.Eval(s).Findings, f => f.Id == "WM-021");
    }

    [Fact]
    public void NavigatorOnlyAndLateToolsAreWarned()
    {
        var b = new BrowserResult("shim", "t", [Fx.Tool("get_info", "Reads the info panel for the user.", annotations: """{"readOnlyHint":true}""", at: 9000)], true, 0, null);
        var f = Fx.Eval(Fx.Snap(x => x.Browser = b)).Findings;
        Assert.Contains(f, x => x.Id == "WM-002");
        Assert.Contains(f, x => x.Id == "WM-033");
    }

    [Fact]
    public void DraftWebMcpErrorsCostAtMostThreePointsEach()
    {
        var s = Fx.Snap(x => x.Browser = Fx.Page(Fx.Tool("x", "y")));
        var r = Fx.Eval(s);
        Assert.True(r.Categories["webmcp"] >= 100 - 3 * 6);
    }

    [Fact]
    public void DotNetFixHintAppearsOnlyOnDotNetSites()
    {
        Snapshot Make(bool dotnet) => Fx.Snap(x =>
        {
            x.Home = FakeFetcher.Resp("https://example.test/", 200, "<html></html>", "text/html", dotnet ? [("Server", "Kestrel")] : []);
            x.Browser = Fx.Page(Fx.Tool("delete_note", "Deletes a note from the notebook for good."));
        });
        Assert.NotNull(Fx.Eval(Make(true)).Findings.First(f => f.Id == "WM-020").Fix.DotNet);
        Assert.Null(Fx.Eval(Make(false)).Findings.First(f => f.Id == "WM-020").Fix.DotNet);
    }

    // ---- x402 ----

    private static string Challenge(string network = "eip155:84532", string asset = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
        string amount = "10000", string payTo = "0x1111111111111111111111111111111111111111", bool resource = true) =>
        $$"""{"x402Version":2,{{(resource ? "\"resource\":{\"url\":\"https://example.test/p\",\"description\":\"d\",\"mimeType\":\"application/json\"}," : "")}}"accepts":[{"scheme":"exact","network":"{{network}}","asset":"{{asset}}","amount":"{{amount}}","payTo":"{{payTo}}","maxTimeoutSeconds":60}]}""";

    private static Snapshot X402(string body, string facilitatorKinds = """{"kinds":[{"scheme":"exact","network":"eip155:84532"}]}""") =>
        Fx.Snap(x => x.X402 = new X402Result(
            [new X402Challenge(new Uri("https://example.test/paid"), JsonNode.Parse(body)!, "body")], [],
            Collector.DefaultTestnetFacilitator, FakeFetcher.Resp("https://x402.org/facilitator/supported", 200, facilitatorKinds, "application/json")));

    [Fact]
    public void ValidChallengePassesEveryX402Check()
    {
        var r = Fx.Eval(X402(Challenge()));
        Assert.DoesNotContain(r.Findings, f => f.Id.StartsWith("X4") && f.Severity != "info");
        Assert.Contains("X4-030", Ids(r));
        Assert.Equal(100, r.Categories["x402"]);
    }

    [Theory]
    [InlineData("X4-011", "0.01", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x1111111111111111111111111111111111111111")]
    [InlineData("X4-011", "10000", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "not-an-address")]
    [InlineData("X4-012", "99999999999", "0x036CbD53842c5426634e7929541eC2318f3dCF7e", "0x1111111111111111111111111111111111111111")]
    [InlineData("X4-013", "10000", "0x2222222222222222222222222222222222222222", "0x1111111111111111111111111111111111111111")]
    public void BadChallengesAreFlagged(string id, string amount, string asset, string payTo)
    {
        var body = Challenge(asset: asset, amount: amount, payTo: payTo).Replace("\"scheme\":\"exact\"", "\"scheme\":\"exact\",\"extra\":{\"name\":\"USDC\"}");
        Assert.Contains(id, Ids(Fx.Eval(X402(body))));
    }

    [Fact]
    public void MissingVersionResourceAndUnsupportedFacilitatorPair()
    {
        var f = Fx.Eval(X402(Challenge(resource: false).Replace("\"x402Version\":2,", ""), """{"kinds":[]}""")).Findings;
        Assert.Contains(f, x => x.Id == "X4-010");
        Assert.Contains(f, x => x.Id == "X4-021");
        Assert.Contains(f, x => x.Id == "X4-020");
    }

    // ---- MCP over a fake server ----

    private static FakeFetcher McpSite(string toolsJson, bool acceptForeignOrigin = false)
    {
        var f = new FakeFetcher();
        f.Add("https://example.test/", 200, "<html></html>", "text/html");
        f.Add("https://example.test/.well-known/mcp/server-card.json", 200, """{"remotes":[{"url":"/mcp"}]}""", "application/json");
        f.Routes["https://example.test/mcp"] = (_, headers, body) =>
        {
            const string url = "https://example.test/mcp";
            if (headers?.ContainsKey("Origin") == true && !acceptForeignOrigin) return FakeFetcher.Resp(url, 403, "no");
            var method = JsonNode.Parse(body!)!["method"]?.ToString();
            return method switch
            {
                "initialize" => FakeFetcher.Resp(url, 200, """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{}}}""", "application/json"),
                "tools/list" => FakeFetcher.Resp(url, 200, """{"jsonrpc":"2.0","id":2,"result":{"tools":""" + toolsJson + "}}", "application/json"),
                _ => FakeFetcher.Resp(url, 202, ""),
            };
        };
        return f;
    }

    private static async Task<Report> ScanMcp(FakeFetcher f) =>
        await new Scanner(f).ScanAsync(new Uri("https://example.test/"));

    [Fact]
    public async Task GoodMcpServerPasses()
    {
        var r = await ScanMcp(McpSite("""[{"name":"get_info","description":"Reads the information for a given item.","inputSchema":{"type":"object","properties":{"id":{"type":"string","description":"id"}},"required":["id"]},"annotations":{"readOnlyHint":true}}]"""));
        Assert.Contains("MCP-002", r.Passed);
        Assert.DoesNotContain(r.Findings, x => x.Id.StartsWith("MCP") && x.Severity != "info");
        Assert.Equal(100, r.Categories["mcp"]);
    }

    [Fact]
    public async Task DestructiveToolAndForeignOriginAreFlagged()
    {
        var r = await ScanMcp(McpSite("""[{"name":"delete_all","description":"x","inputSchema":{"type":"string"}}]""", acceptForeignOrigin: true));
        var ids = Ids(r).ToList();
        Assert.Contains("MCP-020", ids);
        Assert.Contains("MCP-021", ids);
        Assert.Contains("MCP-010", ids);
        Assert.Contains("MCP-011", ids);
        Assert.Contains("MCP-012", ids);
    }

    [Fact]
    public async Task McpEndpointThatDoesNotAnswerIsAnError()
    {
        var f = McpSite("[]");
        f.Routes["https://example.test/mcp"] = (_, _, _) => FakeFetcher.Resp("https://example.test/mcp", 500, "boom");
        Assert.Contains(Ids(await ScanMcp(f)), id => id == "MCP-002");
    }

    [Fact]
    public async Task SseResponsesAreParsed()
    {
        var f = McpSite("[]");
        f.Routes["https://example.test/mcp"] = (_, _, body) => FakeFetcher.Resp("https://example.test/mcp", 200,
            "event: message\ndata: " + (JsonNode.Parse(body!)!["method"]?.ToString() == "initialize"
                ? """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}"""
                : """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}""") + "\n\n", "text/event-stream");
        Assert.Contains("MCP-002", (await ScanMcp(f)).Passed);
    }

    [Fact]
    public async Task X402ProbeReadsTheHeaderAndTheBody()
    {
        var f = new FakeFetcher();
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Challenge()));
        f.Add("https://example.test/", 402, "", "text/html", ("PAYMENT-REQUIRED", b64));
        f.Add("https://x402.org/facilitator/supported", 200, """{"kinds":[{"scheme":"exact","network":"eip155:84532"}]}""", "application/json");
        var r = await new Scanner(f).ScanAsync(new Uri("https://example.test/"));
        Assert.Contains("X4-010", r.Passed);
        Assert.Contains("X4-020", r.Passed);
    }

    // ---- guards and helpers ----

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("192.168.0.9", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("100.64.0.4", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:4700::1111", true)]
    public void PublicAddressRule(string ip, bool expected) => Assert.Equal(expected, UrlGuard.IsPublic(System.Net.IPAddress.Parse(ip)));

    [Fact]
    public async Task GuardedFetcherRefusesLoopback()
    {
        using var fetcher = new GuardedFetcher();
        var r = await fetcher.GetAsync(new Uri("http://127.0.0.1:1/"), CancellationToken.None);
        Assert.NotNull(r.Error);
        Assert.Contains("blocked", r.Error);
        var file = await fetcher.GetAsync(new Uri("file:///etc/passwd"), CancellationToken.None);
        Assert.Contains("blocked", file.Error);
    }

    [Fact]
    public void LinkHeaderAndHtmlLinkAreParsed()
    {
        Assert.Equal("/llms.txt", Collector.LinkRel("</llms.txt>; rel=\"llms-txt\"", "llms-txt"));
        Assert.Equal("/mcp", Collector.HtmlLinkRel("<link rel=\"mcp\" href=\"/mcp\">", "mcp"));
    }
}
