using Levelset.Browser;
using Levelset.Web;
using Microsoft.AspNetCore.Builder;

namespace Levelset.Tests;

/// <summary>Levelset must score 100 on itself: the dogfood gate.</summary>
public class DogfoodTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task LevelsetScoresAHundredOnItself()
    {
        var app = Site.Build(["--urls", "http://127.0.0.1:0", "--Levelset:AllowPrivate=true", "--Levelset:DailyLimit=0"]);
        await app.StartAsync();
        try
        {
            var address = app.Urls.First();
            using var fetcher = new GuardedFetcher(new FetcherOptions { AllowPrivate = true });
            var scanner = new Scanner(fetcher, new PlaywrightBrowserProbe());
            var report = await scanner.ScanAsync(new Uri(address + "/"), new ScanOptions { AllowPrivate = true });

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
            var problems = report.Findings.Where(f => f.Severity != "info").Select(f => $"{f.Id} {f.Target}: {f.Message}").ToList();
            Assert.True(report.Browser is { Mode: "shim" or "native" }, "browser probe did not run: " + string.Join("; ", report.Findings.Where(f => f.Id == "WM-001").Select(f => f.Message)));
            Assert.Contains(report.Findings, f => f.Id == "WM-001" && f.Target == "tool:check_site");
            Assert.Contains("MCP-002", report.Passed);
            Assert.Empty(problems);
            Assert.Equal(100, report.Score);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
