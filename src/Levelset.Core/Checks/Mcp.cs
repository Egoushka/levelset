namespace Levelset;

public static partial class Catalog
{
    private static IEnumerable<Check> McpChecks()
    {
        const Category M = Category.Mcp;
        const string Dn = "Mark MCP tools with Handrail attributes: [McpTool(ReadOnly = true)] or [McpTool(Destructive = true)] (Handrail, the .NET WebMCP library).";
        yield return Def("MCP-001", M, Severity.Info, CheckStatus.Draft, "MCP server discoverable", McpSpec,
            "Publish /.well-known/mcp/server-card.json (draft SEP-1649) or a Link header rel=\"mcp\" so agents find the endpoint.",
            s => s.Mcp is { } m ? One(m.Endpoint!.AbsolutePath, $"found via {m.DiscoveredVia}") : null);
        yield return Def("MCP-002", M, Severity.Error, CheckStatus.Standard, "Endpoint answers initialize with a supported protocol version",
            "https://modelcontextprotocol.io/specification/2025-06-18/basic/transports",
            "Answer a POST of initialize on the MCP endpoint within 5 seconds with a supported protocolVersion (2024-11-05, 2025-03-26, 2025-06-18 or 2025-11-25).",
            s =>
            {
                if (s.Mcp is not { } m) return null;
                if (m.Unauthorized) return Pass;
                var t = m.Endpoint!.AbsolutePath;
                if (m.Initialize is null || m.Initialize.Error is not null) return One(t, "no response: " + m.Initialize?.Error);
                if (m.Initialize.ElapsedMs > 5000) return One(t, $"initialize took {m.Initialize.ElapsedMs} ms");
                if (m.ProtocolVersion is null) return One(t, $"initialize did not return a result (status {m.Initialize.Status})");
                return Collector.IsSupportedProtocol(m.ProtocolVersion) ? Pass : One(t, $"unsupported protocolVersion {m.ProtocolVersion}");
            });
        yield return Def("MCP-003", M, Severity.Warning, CheckStatus.Draft, "Server card matches live tools/list", McpSpec,
            "Regenerate the server card from the live tool list so both name the same tools.",
            s =>
            {
                if (s.Mcp is not { Card: { } card, Tools: { } tools }) return null;
                if (card["tools"] is not System.Text.Json.Nodes.JsonArray arr) return null;
                var cardNames = arr.Select(n => n is System.Text.Json.Nodes.JsonValue v ? v.ToString() : n?["name"]?.ToString() ?? "").ToHashSet();
                var live = tools.Select(t => t.Name ?? "").ToHashSet();
                return cardNames.SetEquals(live) ? Pass : One("server card", "tool names differ from tools/list");
            });
        yield return Def("MCP-010", M, Severity.Error, CheckStatus.Standard, "tools/list returns tools with a description and a valid inputSchema", McpSpec,
            "Return every tool with a name, a description and an inputSchema of type object whose required names exist in properties.",
            s =>
            {
                if (s.Mcp is not { Unauthorized: false } m) return null;
                if (m.Tools is null) return One(m.Endpoint!.AbsolutePath, "tools/list did not return a tool list");
                return Many(m.Tools.SelectMany(t => ToolProblems(t)));
            });
        yield return Def("MCP-011", M, Severity.Warning, CheckStatus.Standard, "Tool descriptions say what the tool does", McpSpec,
            "Write 20 to 400 characters that say what the tool does and when to use it; do not repeat the name.",
            s => McpTools(s) is { } tools ? Many(tools.SelectMany(t => ToolLint.DescriptionProblems(t).Select(p => new Problem(t.Target, p)))) : null);
        yield return Def("MCP-012", M, Severity.Warning, CheckStatus.Standard, "Tools carry annotations consistent with their names", McpSpec,
            "Add annotations: readOnlyHint, destructiveHint and idempotentHint so clients know which tools need confirmation.",
            s => McpTools(s) is { } tools ? Many(tools.SelectMany(t =>
            {
                if (t.Annotations is null) return [new Problem(t.Target, "no annotations")];
                if (t.Flag("readOnlyHint") && ToolLint.LooksLikeWrite(t.Name)) return [new Problem(t.Target, "readOnlyHint is true but the name looks like a write")];
                return Array.Empty<Problem>();
            })) : null,
            snippet: "\"annotations\": { \"readOnlyHint\": true, \"destructiveHint\": false }", dotnet: Dn);
        yield return Def("MCP-013", M, Severity.Warning, CheckStatus.Standard, "tools/list stays small (under 20 KB and 40 tools)", McpSpec,
            "Every tool description is paid for in the agent's context: merge near-duplicate tools and trim descriptions.",
            s =>
            {
                if (s.Mcp is not { Tools: { } tools, ToolsList: { } list }) return null;
                var problems = new List<Problem>();
                if (list.Body.Length > 20 * 1024) problems.Add(new("tools/list", $"payload is {list.Body.Length / 1024} KB"));
                if (tools.Count > 40) problems.Add(new("tools/list", $"{tools.Count} tools"));
                return problems;
            });
        yield return Def("MCP-020", M, Severity.Error, CheckStatus.Standard, "An open server exposes no destructive-looking tool", McpSpec,
            "Require authentication for tools that delete, pay or send, or mark them destructiveHint: true so clients ask first.",
            s => McpTools(s) is { } tools ? Many(tools.Where(t => ToolLintExtra.LooksDestructive(t) && !t.Flag("destructiveHint"))
                .Select(t => new Problem(t.Target, "looks destructive, the server is unauthenticated and destructiveHint is not set"))) : null,
            dotnet: Dn);
        yield return Def("MCP-021", M, Severity.Warning, CheckStatus.Standard, "A foreign Origin is rejected", "https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#security-warning",
            "Validate the Origin header on the MCP endpoint and answer 403 for origins you do not trust, to stop DNS rebinding.",
            s => s.Mcp is { ForeignOriginStatus: { } st } m ? (st is >= 200 and < 300 ? One(m.Endpoint!.AbsolutePath, "accepted a request from a foreign Origin") : Pass) : null);
        yield return Def("MCP-030", M, Severity.Info, CheckStatus.Standard, "OAuth protected-resource metadata", "https://www.rfc-editor.org/rfc/rfc9728",
            "Optional: when the endpoint answers 401, publish /.well-known/oauth-protected-resource.",
            s => s.Mcp is { Unauthorized: true } m ? One(m.Endpoint!.AbsolutePath, m.OAuthMetadataStatus is >= 200 and < 300 ? "OAuth metadata found" : "401 without OAuth metadata") : null);
    }

    private static IReadOnlyList<ToolDescriptor>? McpTools(Snapshot s) => s.Mcp is { Unauthorized: false, Tools: { } t } ? t : null;

    internal static IEnumerable<Problem> ToolProblems(ToolDescriptor t)
    {
        if (ToolLint.NameProblem(t.Name) is { } np) yield return new Problem(t.Target, np);
        if (string.IsNullOrWhiteSpace(t.Description)) yield return new Problem(t.Target, "has no description");
        foreach (var p in ToolLint.SchemaProblems(t.InputSchema)) yield return new Problem(t.Target, p);
    }
}
