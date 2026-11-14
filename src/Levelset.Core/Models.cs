using System.Text.Json.Serialization;

namespace Levelset;

public enum Severity { Info, Warning, Error }

public enum CheckStatus { Standard, Draft, DeFacto, Vendor }

public enum Category { WebMcp, Mcp, X402, Discovery, Hygiene }

/// <summary>A catalog entry: what a check is, how serious, and what to tell the author.</summary>
public sealed record CheckInfo(
    string Id,
    Category Category,
    Severity Severity,
    CheckStatus Status,
    string Title,
    string Spec,
    string FixText,
    string? FixSnippet = null,
    string? FixDotNet = null);

public sealed record Fix(string Text, string? Snippet = null, string? DotNet = null, string? Prompt = null);

public sealed record Finding(string Id, string Severity, string Target, string Message, Fix Fix, string Spec, double Gain = 0);

/// <summary>One failing check, with every place it failed, ranked by the points fixing it adds.</summary>
public sealed record RankedFix(
    int Rank, string Id, string Title, string Severity, string Category, double Gain,
    IReadOnlyList<string> Targets, string Prompt);

public sealed record BrowserInfo(string Mode, string? Chrome);

public sealed record Report(
    string Schema,
    string Url,
    int Score,
    string Grade,
    Dictionary<string, int?> Categories,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> Passed,
    BrowserInfo? Browser,
    long DurationMs,
    IReadOnlyList<RankedFix>? Fixes = null)
{
    public const string SchemaId = "levelset/v1";
}

public static class SeverityNames
{
    public static string Name(this Severity s) => s switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };

    public static string Key(this Category c) => c switch
    {
        Category.WebMcp => "webmcp",
        Category.Mcp => "mcp",
        Category.X402 => "x402",
        Category.Discovery => "discovery",
        _ => "hygiene",
    };
}
