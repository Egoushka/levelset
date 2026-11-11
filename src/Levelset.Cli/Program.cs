using System.Text.Json;
using Levelset;
using Levelset.Browser;

return await Cli.RunAsync(args);

internal static class Cli
{
    private const string Usage = """
        levelset check <url> [options]

          --fail-below <n>        fail when the score is under n (default 0)
          --fail-on-drop <n>      fail when the score fell more than n points against the baseline
          --fail-on <level>       fail on a new finding at this level: error (default), warning or none
          --baseline <file>       baseline file (default levelset-baseline.json); read when it exists
          --update-baseline       write the current result to the baseline file and exit 0
          --json <file>           write the full report
          --markdown <file>       write the new/fixed findings table
          --mcp <url>             MCP endpoint to check when the site does not advertise one
          --paid-path <path>      path to probe for an x402 402 challenge (repeatable)
          --facilitator <url>     x402 facilitator for the /supported check
          --no-browser            skip the WebMCP browser check
          --public-only           refuse localhost and private addresses (the default allows them: this runs in your CI)

        Exit code: 0 passed, 1 gate failed, 2 could not run.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || args[0] != "check" || args.Contains("--help"))
        {
            Console.Error.WriteLine(Usage);
            return args.Contains("--help") ? 0 : 2;
        }

        try
        {
            var o = Parse(args[1..]);
            if (!Uri.TryCreate(o.Url, UriKind.Absolute, out var url) || !UrlGuard.IsWebUrl(url))
                return Fail("the url must be an absolute http or https address");

            using var fetcher = new GuardedFetcher(new FetcherOptions { AllowPrivate = !o.PublicOnly });
            var scanner = new Scanner(fetcher, o.NoBrowser ? null : new PlaywrightBrowserProbe());
            var report = await scanner.ScanAsync(url, new ScanOptions
            {
                AllowPrivate = !o.PublicOnly,
                McpEndpoint = o.Mcp is null ? null : new Uri(o.Mcp),
                PaidPaths = o.PaidPaths,
                Facilitator = o.Facilitator is null ? null : new Uri(o.Facilitator),
                SkipBrowser = o.NoBrowser,
            });

            if (report.Browser is null && !o.NoBrowser) Console.Error.WriteLine("warning: the browser check did not run");
            foreach (var f in report.Findings.Where(f => f.Id == "WM-001" && f.Target == "browser"))
                Console.Error.WriteLine("warning: " + f.Message);

            if (o.UpdateBaseline)
            {
                await File.WriteAllTextAsync(o.BaselinePath, Baseline.From(report).ToJson());
                Console.WriteLine($"Baseline written to {o.BaselinePath}: score {report.Score}");
                await Emit(o, report, Gate.Evaluate(report, null, new GateOptions(FailOn: "none")));
                return 0;
            }

            var baseline = File.Exists(o.BaselinePath) ? Baseline.Parse(await File.ReadAllTextAsync(o.BaselinePath)) : null;
            var gate = Gate.Evaluate(report, baseline, new GateOptions(o.FailBelow, o.FailOnDrop, o.FailOn));
            await Emit(o, report, gate);
            Console.WriteLine($"Levelset {report.Score}/100 grade {report.Grade}: {(gate.Passed ? "passed" : "failed")}");
            foreach (var r in gate.Reasons) Console.WriteLine("  " + r);
            return gate.Passed ? 0 : 1;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidDataException or IOException or JsonException or UriFormatException)
        {
            return Fail(e.Message);
        }
    }

    private static async Task Emit(Options o, Report report, GateResult gate)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        if (o.Json is not null) await File.WriteAllTextAsync(o.Json, JsonSerializer.Serialize(report, options));
        var markdown = Gate.Markdown(report, gate);
        if (o.Markdown is not null) await File.WriteAllTextAsync(o.Markdown, markdown);
        if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } output)
            await File.AppendAllTextAsync(output, $"score={report.Score}\ngrade={report.Grade}\npassed={gate.Passed.ToString().ToLowerInvariant()}\n");
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
            await File.AppendAllTextAsync(summary, markdown);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("levelset: " + message);
        return 2;
    }

    private sealed record Options(
        string Url, int FailBelow, int? FailOnDrop, string FailOn, string BaselinePath, bool UpdateBaseline,
        string? Json, string? Markdown, string? Mcp, List<string> PaidPaths, string? Facilitator, bool NoBrowser, bool PublicOnly);

    private static Options Parse(string[] a)
    {
        string? url = null, json = null, md = null, mcp = null, fac = null;
        var failBelow = 0;
        int? drop = null;
        var failOn = "error";
        var baseline = "levelset-baseline.json";
        bool update = false, noBrowser = false, publicOnly = false;
        var paid = new List<string>();
        for (var i = 0; i < a.Length; i++)
        {
            string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException($"{a[i]} needs a value");
            switch (a[i])
            {
                case "--fail-below": failBelow = int.Parse(Next()); break;
                case "--fail-on-drop": drop = int.Parse(Next()); break;
                case "--fail-on":
                    failOn = Next();
                    if (failOn is not ("error" or "warning" or "none")) throw new ArgumentException("--fail-on takes error, warning or none");
                    break;
                case "--baseline": baseline = Next(); break;
                case "--update-baseline": update = true; break;
                case "--json": json = Next(); break;
                case "--markdown": md = Next(); break;
                case "--mcp": mcp = Next(); break;
                case "--paid-path": paid.Add(Next()); break;
                case "--facilitator": fac = Next(); break;
                case "--no-browser": noBrowser = true; break;
                case "--public-only": publicOnly = true; break;
                default:
                    if (a[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"unknown option {a[i]}");
                    url = a[i];
                    break;
            }
        }

        return new Options(url ?? throw new ArgumentException("a url is required"), failBelow, drop, failOn, baseline, update,
            json, md, mcp, paid, fac, noBrowser, publicOnly);
    }
}
