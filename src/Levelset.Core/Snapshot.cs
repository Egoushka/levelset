using System.Text.Json.Nodes;

namespace Levelset;

public sealed record BrowserResult(
    string Mode,
    string? Chrome,
    IReadOnlyList<ToolDescriptor> Tools,
    bool NavigatorOnly,
    int RenderedTextLength,
    string? Error);

/// <summary>Loads a page in a real browser with a recorder that never runs a tool. Implemented in Levelset.Browser.</summary>
public interface IBrowserProbe
{
    Task<BrowserResult> ProbeAsync(Uri url, bool allowPrivate, CancellationToken ct);
}

public sealed record McpResult(
    Uri? Endpoint,
    string? DiscoveredVia,
    Uri? CardUrl,
    JsonNode? Card,
    FetchResult? Initialize,
    string? ProtocolVersion,
    FetchResult? ToolsList,
    IReadOnlyList<ToolDescriptor>? Tools,
    int? ForeignOriginStatus,
    bool Unauthorized,
    int? OAuthMetadataStatus);

public sealed record X402Challenge(Uri Source, JsonNode Body, string Origin);

public sealed record X402Result(
    IReadOnlyList<X402Challenge> Challenges,
    IReadOnlyList<string> PaidToolHints,
    Uri? Facilitator,
    FetchResult? FacilitatorSupported);

public sealed class Snapshot
{
    public required Uri Url { get; init; }
    public required ScanOptions Options { get; init; }
    public FetchResult? Home { get; set; }
    public FetchResult? Robots { get; set; }
    public FetchResult? Llms { get; set; }
    public FetchResult? LlmsFull { get; set; }
    public FetchResult? Markdown { get; set; }
    public FetchResult? Sitemap { get; set; }
    public FetchResult? ApiCatalog { get; set; }
    public FetchResult? AgentCard { get; set; }
    public List<(string Link, FetchResult Result)> LlmsLinks { get; } = [];
    public McpResult? Mcp { get; set; }
    public X402Result? X402 { get; set; }
    public BrowserResult? Browser { get; set; }

    public bool IsDotNet =>
        Home is { } h
        && ((h.Header("server")?.Contains("Kestrel", StringComparison.OrdinalIgnoreCase) ?? false)
            || (h.Header("x-powered-by")?.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase) ?? false)
            || h.Body.Contains("_framework/", StringComparison.Ordinal));

    public Uri Resolve(string path) => new(Url, path);
}

public sealed class ScanOptions
{
    /// <summary>Allow loopback and private targets: the CLI and Action scan localhost, the hosted service does not.</summary>
    public bool AllowPrivate { get; init; }
    public Uri? McpEndpoint { get; init; }
    public IReadOnlyList<string> PaidPaths { get; init; } = [];
    public Uri? Facilitator { get; init; }
    public bool SkipBrowser { get; init; }
}
