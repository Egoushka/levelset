using System.Diagnostics;

namespace Levelset;

public sealed class Scanner(IFetcher fetcher, IBrowserProbe? browser = null)
{
    public async Task<Report> ScanAsync(Uri url, ScanOptions? options = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new ScanOptions();
        var snapshot = await new Collector(fetcher, browser).CollectAsync(url, options, ct);
        return Evaluate(snapshot, sw.ElapsedMilliseconds);
    }

    public static Report Evaluate(Snapshot s, long durationMs)
    {
        var findings = new List<Finding>();
        var passed = new List<string>();
        var failed = new List<CheckInfo>();
        var applicable = new HashSet<Category> { Category.Discovery, Category.Hygiene };
        var dotnet = s.IsDotNet;
        foreach (var check in Catalog.All)
        {
            var problems = check.Run(s);
            if (problems is null) continue;
            var info = check.Info;
            if (info.Severity != Severity.Info) applicable.Add(info.Category);
            if (problems.Count == 0)
            {
                if (info.Severity != Severity.Info) passed.Add(info.Id);
                continue;
            }

            if (info.Severity != Severity.Info) failed.Add(info);
            findings.AddRange(problems.Select(p => new Finding(info.Id, info.Severity.Name(), p.Target, p.Message,
                new Fix(info.FixText, info.FixSnippet, dotnet ? info.FixDotNet : null), info.Spec)));
        }

        var (total, categories) = Scorer.Score(applicable, failed);
        var browser = s.Browser is { } b ? new BrowserInfo(b.Mode, b.Chrome) : null;
        return new Report(Report.SchemaId, s.Url.ToString(), total, Scorer.Grade(total), categories,
            findings.OrderByDescending(f => f.Severity == "error").ThenByDescending(f => f.Severity == "warning").ToList(),
            passed, browser, durationMs);
    }
}
