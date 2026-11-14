using System.Text.Json;
using System.Text.Json.Nodes;
using Levelset.Web;
using Microsoft.AspNetCore.Builder;

namespace Levelset.Tests;

public class AgentOutputTests
{
    private static readonly IReadOnlySet<Category> Both = new HashSet<Category> { Category.Discovery, Category.Hygiene };

    private static CheckInfo Check(string id, Category c, Severity s, CheckStatus st = CheckStatus.Standard) =>
        new(id, c, s, st, "t", "https://example.test", "fix");

    [Fact, Ac("AC-P01-1")]
    public void NoFailuresIsAHundredAndNothingToFix()
    {
        Assert.Equal(100, Scorer.ScoreExact(Both, []).Total);
        var r = new Report(Report.SchemaId, "https://x.test/", 100, "A", new() { ["discovery"] = 100, ["hygiene"] = 100 }, [], [], null, 0, []);
        var a = AgentReport.From(r);
        Assert.True(a.Passed);
        Assert.Empty(a.Fixes);
        Assert.Contains("Nothing to fix", a.Prompt);
    }

    [Fact, Ac("AC-P01-2")]
    public void GainIsTheScoreDropAvoided()
    {
        var error = Check("A-1", Category.Discovery, Severity.Error);
        var warn = Check("A-2", Category.Hygiene, Severity.Warning);
        var failed = new[] { error, warn };
        var all = Scorer.ScoreExact(Both, failed).Total;
        Assert.Equal(Math.Round(Scorer.ScoreExact(Both, [warn]).Total - all, 1), Scorer.Gain(Both, failed, error));
        Assert.Equal(8.6, Scorer.Gain(Both, failed, error)); // 12 * 25 / 35
        Assert.Equal(1.1, Scorer.Gain(Both, failed, warn)); // 4 * 10 / 35
    }

    [Fact, Ac("AC-P01-2")]
    public void FixesAreRankedByGainThenSeverityThenId()
    {
        var s = Fx.Snap(x => x.Robots = FakeFetcher.Resp("https://example.test/robots.txt", 200, "User-agent: *\nDisallow: /"));
        var r = Fx.Eval(s);
        var fixes = r.Fixes!;
        Assert.NotEmpty(fixes);
        Assert.Equal(Enumerable.Range(1, fixes.Count), fixes.Select(f => f.Rank));
        Assert.Equal(fixes.OrderByDescending(f => f.Gain).Select(f => f.Gain), fixes.Select(f => f.Gain));
        Assert.All(r.Findings.Where(f => f.Severity != "info"), f => Assert.True(f.Gain > 0 || f.Severity == "warning"));
    }

    [Fact, Ac("AC-P01-3")]
    public async Task BareSiteFixtureScoresAsSaved()
    {
        var r = await SiteFixture.ScanAsync("bare-site");
        Assert.Equal(86, r.Score);
        Assert.Equal("B", r.Grade);
        Assert.Equal(["DISC-001", "DISC-010", "DISC-020", "DISC-021", "DISC-041"], r.Fixes!.Select(f => f.Id));
        Assert.All(r.Fixes!, f => Assert.Equal(2.9, f.Gain));

        var ready = await SiteFixture.ScanAsync("ready-site");
        Assert.Equal(97, ready.Score);
        Assert.Equal(["DISC-020"], ready.Fixes!.Select(f => f.Id));
    }

    [Fact, Ac("AC-P01-4")]
    public async Task AgentFormatCarriesGradesWeightsDecisionAndPrompt()
    {
        var a = AgentReport.From(await SiteFixture.ScanAsync("bare-site"));
        Assert.Equal("levelset-agent/v1", a.Schema);
        Assert.Equal(new AgentCategory("discovery", 80, "B", 25), a.Categories.Single(c => c.Name == "discovery"));
        Assert.Equal(new AgentCategory("hygiene", 100, "A", 10), a.Categories.Single(c => c.Name == "hygiene"));
        Assert.True(a.Passed);
        var order = new[] { "DISC-001", "DISC-010", "DISC-020" }.Select(i => a.Prompt.IndexOf("Fix " + i, StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i > 0));
        Assert.Equal(order.OrderBy(i => i), order);

        var http = new Snapshot { Url = new Uri("http://example.test/"), Options = new ScanOptions() };
        http.Home = FakeFetcher.Resp("http://example.test/", 200, "<html></html>", "text/html");
        var failing = Fx.Eval(http);
        Assert.Contains(failing.Findings, f => f.Severity == "error");
        Assert.False(AgentReport.From(failing).Passed);
        Assert.Equal("levelset-agent/v1", JsonNode.Parse(a.ToJson())!["schema"]!.ToString());
    }

    [Fact, Ac("AC-P01-5")]
    public void FormatFollowsTheFlagThenWhetherOutputIsPiped()
    {
        Assert.Equal("agent", AgentReport.ChooseFormat(null, true));
        Assert.Equal("text", AgentReport.ChooseFormat(null, false));
        Assert.Equal("text", AgentReport.ChooseFormat("text", true));
        Assert.Equal("agent", AgentReport.ChooseFormat("agent", false));
        Assert.Throws<ArgumentException>(() => AgentReport.ChooseFormat("yaml", false));
    }

    [Fact, Ac("AC-P02-1")]
    public void EveryCatalogCheckYieldsAFullPrompt()
    {
        Assert.All(Catalog.All, c =>
        {
            var p = FixPrompts.ForCheck("https://x.test/", c.Info, [new Problem("/target", "it broke")], null);
            Assert.Contains(c.Info.Id, p);
            Assert.Contains("/target", p);
            Assert.Contains(c.Info.FixText, p);
            Assert.Contains("Done when", p);
        });
    }

    [Fact, Ac("AC-P02-2")]
    public void DotNetVariantAndSnippetOnlyWhenPresent()
    {
        var plain = new CheckInfo("T-1", Category.Discovery, Severity.Warning, CheckStatus.Standard, "t", "https://s.test", "do it");
        var rich = plain with { FixSnippet = "SNIPPET", FixDotNet = "DOTNET" };
        var problems = new[] { new Problem("/", "m") };
        var without = FixPrompts.ForCheck("https://x.test/", rich, problems, null);
        Assert.Contains("SNIPPET", without);
        Assert.DoesNotContain("DOTNET", without);
        Assert.Contains("DOTNET", FixPrompts.ForCheck("https://x.test/", rich, problems, "DOTNET"));
        Assert.DoesNotContain("snippet", FixPrompts.ForCheck("https://x.test/", plain, problems, null));
    }

    [Fact, Ac("AC-P02-3")]
    public async Task BareSitePromptsAreCompleteAndNeverAskToRunATool()
    {
        var r = await SiteFixture.ScanAsync("bare-site");
        var llms = r.Findings.Single(f => f.Id == "DISC-001").Fix.Prompt!;
        Assert.Contains("Fix DISC-001 (llms.txt exists and is well formed) for https://bare.test/.", llms);
        Assert.Contains("- /llms.txt: no /llms.txt (status 404)", llms);
        Assert.Contains("Start from this snippet:", llms);
        Assert.EndsWith("Done when `levelset check https://bare.test/` no longer reports DISC-001.", llms);
        Assert.All(r.Findings, f =>
        {
            var p = f.Fix.Prompt!.ToLowerInvariant();
            Assert.DoesNotContain("call the tool", p);
            Assert.DoesNotContain("submit the form", p);
            Assert.DoesNotContain("send a payment", p);
        });
    }

    [Fact, Ac("AC-ACT-1")]
    public void PullRequestCommentShowsGainAndAPromptCappedAtTen()
    {
        var findings = Enumerable.Range(1, 12).Select(i =>
            new Finding($"T-{i:000}", "warning", "/", "m", new Fix("fix", Prompt: $"prompt {i}"), "https://x.test", i)).ToList();
        var report = new Report(Report.SchemaId, "https://x.test/", 50, "D", new(), findings, [], null, 0, []);
        var md = Gate.Markdown(report, Gate.Evaluate(report, null, new GateOptions()));
        Assert.Contains("| Points |", md);
        Assert.Contains("| +3.0 |", md);
        Assert.Contains("Prompt for your coding agent", md);
        Assert.Contains("2 more not shown", md);
        Assert.Equal(10, md.Split('\n').Count(l => l.StartsWith("| T-", StringComparison.Ordinal)));
        Assert.Contains("1. prompt 12", md); // highest gain first
        Assert.DoesNotContain("prompt 1\n", md.Replace("prompt 12", ""));
    }

    private static async Task<(WebApplication App, HttpClient Http)> Site()
    {
        var app = Levelset.Web.Site.Build(["--urls", "http://127.0.0.1:0", "--Levelset:AllowPrivate=true", "--Levelset:DailyLimit=0"]);
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    [Fact, Ac("AC-API-1")]
    public async Task ApiFormatSelectsTheShape()
    {
        var (app, http) = await Site();
        try
        {
            var target = Uri.EscapeDataString(http.BaseAddress + "robots.txt");
            var agent = JsonNode.Parse(await http.GetStringAsync($"/api/v1/check?url={target}&format=agent"))!;
            Assert.Equal("levelset-agent/v1", agent["schema"]!.ToString());
            var full = JsonNode.Parse(await http.GetStringAsync($"/api/v1/check?url={target}"))!;
            Assert.Equal("levelset/v1", full["schema"]!.ToString());
            Assert.NotNull(full["fixes"]);
            Assert.Equal(400, (int)(await http.GetAsync($"/api/v1/check?url={target}&format=yaml")).StatusCode);
        }
        finally { http.Dispose(); await app.StopAsync(); }
    }

    [Fact, Ac("AC-MCP-1")]
    public async Task McpToolTakesAFormat()
    {
        var (app, http) = await Site();
        try
        {
            async Task<JsonNode> Rpc(string json)
            {
                var res = await http.PostAsync("/mcp", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
                return JsonNode.Parse(await res.Content.ReadAsStringAsync())!["result"]!;
            }

            var list = await Rpc("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
            Assert.NotNull(list["tools"]![0]!["inputSchema"]!["properties"]!["format"]);
            var url = http.BaseAddress + "robots.txt";
            var call = await Rpc(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = "check_site", arguments = new { url, format = "agent" } },
            }));
            Assert.Equal("levelset-agent/v1", call["structuredContent"]!["schema"]!.ToString());
        }
        finally { http.Dispose(); await app.StopAsync(); }
    }

    [Fact, Ac("AC-P04-1")]
    public async Task BadgeShowsGradeAndFallsBackToUnknown()
    {
        var (app, http) = await Site();
        try
        {
            var ok = await http.GetAsync("/badge.svg?url=" + Uri.EscapeDataString(http.BaseAddress + "robots.txt"));
            Assert.Equal("image/svg+xml", ok.Content.Headers.ContentType!.MediaType);
            Assert.Matches(@"agent ready: [A-F] \d+", await ok.Content.ReadAsStringAsync());
            var bad = await http.GetAsync("/badge.svg?url=nonsense");
            Assert.Equal(200, (int)bad.StatusCode);
            Assert.Contains("agent ready: unknown", await bad.Content.ReadAsStringAsync());
        }
        finally { http.Dispose(); await app.StopAsync(); }
    }

    [Fact]
    public void BadgeEscapesNothingItCannotControl()
    {
        var svg = Badge.Svg(new Report(Report.SchemaId, "https://x.test/", 97, "A", new(), [], [], null, 0));
        Assert.Contains("A 97", svg);
        Assert.DoesNotContain("<script", svg);
    }
}
