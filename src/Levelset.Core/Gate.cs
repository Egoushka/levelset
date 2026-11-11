using System.Text;
using System.Text.Json;

namespace Levelset;

public sealed record BaselineFinding(string Id, string Target, string Severity);

public sealed record Baseline(string Schema, string Url, int Score, IReadOnlyList<BaselineFinding> Findings)
{
    public const string SchemaId = "levelset-baseline/v1";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Baseline From(Report r) =>
        new(SchemaId, r.Url, r.Score, r.Findings.Where(f => f.Severity != "info").Select(f => new BaselineFinding(f.Id, f.Target, f.Severity)).ToList());

    public string ToJson() => JsonSerializer.Serialize(this, Options) + "\n";

    public static Baseline Parse(string json) =>
        JsonSerializer.Deserialize<Baseline>(json, Options) is { Schema: SchemaId } b
            ? b
            : throw new InvalidDataException("not a levelset baseline file");
}

public sealed record GateOptions(int FailBelow = 0, int? FailOnDrop = null, string FailOn = "error");

public sealed record GateResult(
    bool Passed,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<Finding> New,
    IReadOnlyList<BaselineFinding> Fixed,
    int? ScoreDelta);

public static class Gate
{
    private static int Rank(string severity) => severity switch { "error" => 2, "warning" => 1, _ => 0 };

    /// <summary>
    /// Findings that count as new are those not in the baseline (the same ID and target); with no baseline every
    /// non-info finding is new. The run fails below the floor, on a drop larger than FailOnDrop, or on a new
    /// finding at or above FailOn ("error", "warning" or "none").
    /// </summary>
    public static GateResult Evaluate(Report report, Baseline? baseline, GateOptions o)
    {
        var known = baseline?.Findings.Select(f => (f.Id, f.Target)).ToHashSet() ?? [];
        var current = report.Findings.Where(f => f.Severity != "info").ToList();
        var added = current.Where(f => !known.Contains((f.Id, f.Target))).ToList();
        var now = current.Select(f => (f.Id, f.Target)).ToHashSet();
        var fixedOnes = baseline?.Findings.Where(f => !now.Contains((f.Id, f.Target))).ToList() ?? [];
        int? delta = baseline is null ? null : report.Score - baseline.Score;

        var reasons = new List<string>();
        if (report.Score < o.FailBelow) reasons.Add($"score {report.Score} is below the floor of {o.FailBelow}");
        if (o.FailOnDrop is { } drop && delta is { } d && -d > drop)
            reasons.Add($"score dropped {-d} points against the baseline (allowed {drop})");
        if (o.FailOn != "none")
        {
            var threshold = Rank(o.FailOn);
            var offending = added.Where(f => Rank(f.Severity) >= threshold).ToList();
            if (offending.Count > 0)
                reasons.Add($"{offending.Count} new {o.FailOn}-level finding(s): " + string.Join(", ", offending.Take(5).Select(f => $"{f.Id} {f.Target}")));
        }

        return new GateResult(reasons.Count == 0, reasons, added, fixedOnes, delta);
    }

    public static string Markdown(Report report, GateResult g)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### Levelset: {report.Score} / 100 (grade {report.Grade}) {(g.Passed ? "passed" : "failed")}");
        sb.AppendLine();
        if (g.ScoreDelta is { } d) sb.AppendLine($"Against the baseline: {(d >= 0 ? "+" : "")}{d} points.\n");
        foreach (var r in g.Reasons) sb.AppendLine($"- {r}");
        if (g.Reasons.Count > 0) sb.AppendLine();
        if (g.New.Count > 0)
        {
            sb.AppendLine("New findings\n\n| ID | Severity | Where | What | Fix |\n|---|---|---|---|---|");
            foreach (var f in g.New) sb.AppendLine($"| {f.Id} | {f.Severity} | {Cell(f.Target)} | {Cell(f.Message)} | {Cell(f.Fix.Text)} |");
            sb.AppendLine();
        }

        if (g.Fixed.Count > 0)
        {
            sb.AppendLine("Fixed since the baseline\n");
            foreach (var f in g.Fixed) sb.AppendLine($"- {f.Id} {f.Target}");
            sb.AppendLine();
        }

        if (g.New.Count == 0 && g.Fixed.Count == 0) sb.AppendLine("No findings changed.");
        return sb.ToString();
    }

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
