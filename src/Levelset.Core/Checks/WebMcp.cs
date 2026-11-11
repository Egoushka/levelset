namespace Levelset;

public static partial class Catalog
{
    private static IReadOnlyList<ToolDescriptor>? PageTools(Snapshot s) =>
        s.Browser is { Error: null, Tools.Count: > 0 } b ? b.Tools : null;

    // WebMCP is a draft: every check here is CheckStatus.Draft, so a failure costs at most 3 points even where
    // the severity is Error.
    private static IEnumerable<Check> WebMcpChecks()
    {
        const Category W = Category.WebMcp;
        const CheckStatus D = CheckStatus.Draft;
        const string Dn = "[WebMcpTool(Consequential = true)] on the method (Handrail, the .NET WebMCP library).";
        yield return Def("WM-001", W, Severity.Info, D, "Page exposes WebMCP tools", WebMcpSpec,
            "Informational: register tools with document.modelContext.registerTool, or mark a form with toolname and tooldescription.",
            s => s.Browser switch
            {
                null => null,
                { Error: { } e } => One("browser", "the browser probe failed: " + e),
                { Tools.Count: > 0 } b => Many(b.Tools.Select(t => new Problem(t.Target, $"{t.Source} tool ({b.Mode} mode)"))),
                _ => Pass,
            });
        yield return Def("WM-002", W, Severity.Warning, D, "Tools are registered on document.modelContext", WebMcpSpec,
            "The spec defines modelContext on Document. Register tools on document.modelContext; navigator is only a fallback.",
            s => PageTools(s) is not null ? (s.Browser!.NavigatorOnly ? One("page", "tools were reached only through the navigator fallback, not document.modelContext") : Pass) : null,
            snippet: "document.modelContext.registerTool({ name, description, inputSchema, execute });");
        yield return Def("WM-010", W, Severity.Error, D, "Each tool has a valid name, a description and an execute function", WebMcpSpec,
            "Give every tool a name of 1 to 128 characters from A-Z a-z 0-9 _ . -, a non-empty description and an execute function.",
            s => PageTools(s) is { } tools ? Many(tools.SelectMany(t =>
            {
                var p = ToolProblems(t).Where(x => !x.Message.Contains("inputSchema") && !x.Message.Contains("required name") && !x.Message.StartsWith("property")).ToList();
                if (!t.HasExecute) p.Add(new Problem(t.Target, "has no execute function"));
                return p;
            })) : null,
            dotnet: Dn);
        yield return Def("WM-011", W, Severity.Error, D, "inputSchema is valid JSON Schema", WebMcpSpec,
            "Use an inputSchema of type object whose required names all exist in properties and whose properties have a valid type.",
            s => PageTools(s) is { } tools ? Many(tools.SelectMany(t => ToolLint.SchemaProblems(t.InputSchema).Select(p => new Problem(t.Target, p)))) : null,
            snippet: "inputSchema: { type: \"object\", properties: { url: { type: \"string\", description: \"Site to check\" } }, required: [\"url\"] }");
        yield return Def("WM-012", W, Severity.Warning, D, "Descriptions say what the tool does", WebMcpSpec,
            "Write 20 to 400 characters that say what the tool does and when to use it; do not repeat the name.",
            s => PageTools(s) is { } tools ? Many(tools.SelectMany(t => ToolLint.DescriptionProblems(t).Select(p => new Problem(t.Target, p)))) : null);
        yield return Def("WM-013", W, Severity.Warning, D, "Every property has a description", WebMcpSpec,
            "Describe each property in the schema (declarative forms: toolparamdescription, a label or aria-description).",
            s => PageTools(s) is { } tools ? Many(tools.SelectMany(t => ToolLint.UndescribedProperties(t).Select(p => new Problem(t.Target, $"property \"{p}\" has no description")))) : null);
        yield return Def("WM-014", W, Severity.Warning, D, "Tools that return data declare an outputSchema", WebMcpSpec,
            "Add an outputSchema so an agent knows the shape of the result.",
            s => PageTools(s) is { } tools ? Many(tools.Where(t => t.Source == "imperative" && !ToolLint.LooksStateChanging(t) && t.OutputSchema is null)
                .Select(t => new Problem(t.Target, "no outputSchema"))) : null);
        yield return Def("WM-020", W, Severity.Error, D, "A state-changing tool is marked consequential", WebMcpSpec,
            "Mark tools that change state with consequentialHint: true so the browser asks the user first; mark pure reads readOnlyHint: true.",
            s => PageTools(s) is { } tools ? Many(tools.Where(t => t.Source == "imperative" && ToolLint.LooksStateChanging(t)
                    && !t.Flag("consequentialHint") && !t.Flag("readOnlyHint"))
                .Select(t => new Problem(t.Target, "looks state-changing but consequentialHint is not set"))) : null,
            snippet: "annotations: { consequentialHint: true }", dotnet: Dn);
        yield return Def("WM-021", W, Severity.Error, D, "A state-changing form does not auto-submit", WebMcpSpec,
            "Remove toolautosubmit from forms that change state so the user confirms the submit.",
            s => PageTools(s) is { } tools ? Many(tools.Where(t => t.Source == "declarative" && t.AutoSubmit && ToolLint.LooksStateChanging(t))
                .Select(t => new Problem(t.Target, "state-changing form has toolautosubmit"))) : null);
        yield return Def("WM-022", W, Severity.Warning, D, "readOnlyHint does not contradict the name", WebMcpSpec,
            "Remove readOnlyHint: true or rename the tool; a name that reads like a write must not claim read-only.",
            s => PageTools(s) is { } tools ? Many(tools.Where(t => t.Flag("readOnlyHint") && ToolLint.LooksLikeWrite(t.Name))
                .Select(t => new Problem(t.Target, "readOnlyHint is true but the name looks like a write"))) : null);
        yield return Def("WM-023", W, Severity.Warning, D, "Tools returning user content set untrustedContentHint", WebMcpSpec,
            "Set untrustedContentHint: true on tools that return comments, reviews or messages so clients treat the output as untrusted.",
            s => PageTools(s) is { } tools ? Many(tools.Where(t => ToolLint.ReturnsUserContent(t) && !t.Flag("untrustedContentHint"))
                .Select(t => new Problem(t.Target, "returns user-generated content without untrustedContentHint"))) : null);
        yield return Def("WM-030", W, Severity.Warning, D, "No more than 40 tools on a page", "https://developer.chrome.com/docs/lighthouse/agentic-browsing/registered-webmcp-tools",
            "Split the page's tools: every tool costs the agent context.",
            s => PageTools(s) is { } tools && tools.Count > 40 ? One("page", $"{tools.Count} tools") : PageTools(s) is null ? null : Pass);
        yield return Def("WM-033", W, Severity.Warning, D, "Tools are registered within 5 seconds of load", WebMcpSpec,
            "Register tools on page load so an agent sees them without waiting.",
            s => PageTools(s) is { } tools ? Many(tools.Where(t => t.AtMs > 5000).Select(t => new Problem(t.Target, $"registered after {t.AtMs} ms"))) : null);
    }
}
