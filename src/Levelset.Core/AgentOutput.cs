using System.Text;
using System.Text.Json;

namespace Levelset;

public sealed record AgentCategory(string Name, int Score, string Grade, int Weight);

/// <summary>The compact form of a report for a coding agent: the decision, the ranked fixes and one prompt.</summary>
public sealed record AgentReport(
    string Schema,
    string Url,
    int Score,
    string Grade,
    bool Passed,
    IReadOnlyList<AgentCategory> Categories,
    IReadOnlyList<RankedFix> Fixes,
    string Prompt)
{
    public const string SchemaId = "levelset-agent/v1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AgentReport From(Report r)
    {
        var fixes = r.Fixes ?? [];
        var categories = Scorer.Weights
            .Where(w => r.Categories.GetValueOrDefault(w.Key.Key()) is not null)
            .Select(w =>
            {
                var score = r.Categories[w.Key.Key()]!.Value;
                return new AgentCategory(w.Key.Key(), score, Scorer.Grade(score), w.Value);
            }).ToList();
        return new AgentReport(SchemaId, r.Url, r.Score, r.Grade, r.Findings.All(f => f.Severity != "error"),
            categories, fixes, FixPrompts.Combine(r.Url, fixes.Select(f => (f.Id, f.Prompt))));
    }

    /// <summary>An explicit format wins; otherwise JSON for an agent when output is piped, text on a terminal.</summary>
    public static string ChooseFormat(string? flag, bool redirected) => flag switch
    {
        null => redirected ? "agent" : "text",
        "text" or "agent" => flag,
        _ => throw new ArgumentException("--format takes text or agent"),
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Levelset {Score}/100 grade {Grade} for {Url}");
        sb.AppendLine(string.Join(", ", Categories.Select(c => $"{c.Name} {c.Score} ({c.Grade})")));
        foreach (var f in Fixes) sb.AppendLine($"  {f.Rank}. +{f.Gain:0.0} {f.Id} {f.Severity}: {f.Title} ({string.Join(", ", f.Targets.Take(3))})");
        return sb.ToString().TrimEnd();
    }
}
