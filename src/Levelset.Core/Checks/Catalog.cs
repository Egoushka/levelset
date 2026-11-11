namespace Levelset;

public sealed record Problem(string Target, string Message);

/// <summary>A check returns null when it does not apply, no problems when it passes.</summary>
public sealed record Check(CheckInfo Info, Func<Snapshot, IReadOnlyList<Problem>?> Run);

public static partial class Catalog
{
    public const string WebMcpSpec = "https://webmachinelearning.github.io/webmcp/";
    public const string McpSpec = "https://modelcontextprotocol.io/specification/2025-06-18";
    public const string X402Spec = "https://github.com/coinbase/x402";
    public const string LlmsSpec = "https://llmstxt.org/";

    private static readonly Lazy<IReadOnlyList<Check>> AllLazy = new(() =>
        DiscoveryChecks().Concat(HygieneChecks()).Concat(McpChecks()).Concat(X402Checks()).Concat(WebMcpChecks()).ToList());

    public static IReadOnlyList<Check> All => AllLazy.Value;

    private static Check Def(string id, Category cat, Severity sev, CheckStatus status, string title, string spec,
        string fix, Func<Snapshot, IReadOnlyList<Problem>?> run, string? snippet = null, string? dotnet = null) =>
        new(new CheckInfo(id, cat, sev, status, title, spec, fix, snippet, dotnet), run);

    private static readonly IReadOnlyList<Problem> Pass = [];

    private static IReadOnlyList<Problem> One(string target, string message) => [new Problem(target, message)];

    private static IReadOnlyList<Problem> Many(IEnumerable<Problem> problems) => problems.ToList();
}
