using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Levelset;

/// <summary>A tool as a site describes it: a WebMCP registration, a declarative form or an MCP tool.</summary>
public sealed record ToolDescriptor(
    string Source,
    string? Name,
    string? Description,
    JsonNode? InputSchema,
    JsonNode? OutputSchema,
    JsonObject? Annotations,
    bool HasExecute = true,
    int AtMs = 0,
    bool AutoSubmit = false,
    string? Method = null)
{
    public string Target => $"tool:{Name ?? "(unnamed)"}";

    public bool Flag(string annotation) =>
        Annotations?[annotation] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public bool HasAnnotation(string annotation) => Annotations?[annotation] is not null;
}

public static partial class ToolLint
{
    private static readonly HashSet<string> WriteVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "remove", "send", "pay", "purchase", "buy", "submit", "order", "update", "create", "post",
        "write", "drop", "destroy", "transfer", "cancel", "book", "publish", "edit", "set", "reset", "revoke",
    };

    private static readonly HashSet<string> JsonTypes = new() { "object", "array", "string", "number", "integer", "boolean", "null" };

    private static readonly string[] UserContentWords = ["comment", "review", "email", "message", "inbox", "post", "feedback", "ticket"];

    public static IEnumerable<string> Words(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : WordSplit().Split(CamelBoundary().Replace(text, " ")).Where(w => w.Length > 0);

    public static bool LooksStateChanging(ToolDescriptor t)
    {
        if (t.Method is not null && t.Method.Equals("post", StringComparison.OrdinalIgnoreCase)) return true;
        var first = Words(t.Description).FirstOrDefault();
        return Words(t.Name).Any(WriteVerbs.Contains) || (first is not null && WriteVerbs.Contains(first));
    }

    public static bool LooksLikeWrite(string? name) => Words(name).Any(WriteVerbs.Contains);

    public static bool ReturnsUserContent(ToolDescriptor t) =>
        Words(t.Name).Any(w => UserContentWords.Contains(w.ToLowerInvariant())) && !LooksStateChanging(t);

    public static string? NameProblem(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "has no name";
        if (name.Length > 128) return "name is longer than 128 characters";
        return NameChars().IsMatch(name) ? null : "name has characters outside A-Z a-z 0-9 _ . -";
    }

    public static IEnumerable<string> SchemaProblems(JsonNode? schema)
    {
        if (schema is null) { yield return "has no inputSchema"; yield break; }
        if (schema is not JsonObject obj) { yield return "inputSchema is not an object"; yield break; }
        if (obj["type"]?.ToString() != "object") yield return "inputSchema.type must be \"object\"";
        var props = obj["properties"] as JsonObject;
        if (obj["properties"] is not null && props is null) yield return "inputSchema.properties is not an object";
        if (obj["required"] is not null)
        {
            if (obj["required"] is not JsonArray req) yield return "inputSchema.required is not an array";
            else
                foreach (var r in req)
                    if (r is not JsonValue rv || !rv.TryGetValue<string>(out var rn))
                        yield return "inputSchema.required holds a non-string";
                    else if (props?.ContainsKey(rn) != true)
                        yield return $"required name \"{r}\" is not in properties";
        }

        foreach (var (name, p) in props ?? [])
            foreach (var problem in PropertyProblems(name, p))
                yield return problem;
    }

    private static IEnumerable<string> PropertyProblems(string name, JsonNode? p)
    {
        if (p is not JsonObject po) { yield return $"property \"{name}\" is not a schema object"; yield break; }
        if (po.ContainsKey("$ref") || po.ContainsKey("anyOf") || po.ContainsKey("oneOf") || po.ContainsKey("allOf")
            || po.ContainsKey("enum") || po.ContainsKey("const")) yield break;
        var types = po["type"] switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => new[] { s },
            JsonArray a => a.Select(x => x?.ToString() ?? "").ToArray(),
            _ => null,
        };
        if (types is null) yield return $"property \"{name}\" has no type";
        else if (types.Any(t => !JsonTypes.Contains(t))) yield return $"property \"{name}\" has an unknown type";
    }

    public static IEnumerable<string> DescriptionProblems(ToolDescriptor t)
    {
        var d = t.Description?.Trim() ?? "";
        if (d.Length < 20) yield return "description is shorter than 20 characters";
        else if (d.Length > 400) yield return "description is longer than 400 characters";
        else if (Normalize(d) == Normalize(t.Name) || Words(d).Count() < 4)
            yield return "description does not say what the tool does";
    }

    public static IEnumerable<string> UndescribedProperties(ToolDescriptor t) =>
        t.InputSchema?["properties"] is JsonObject props
            ? props.Where(p => string.IsNullOrWhiteSpace(p.Value?["description"]?.ToString())).Select(p => p.Key)
            : [];

    private static string Normalize(string? s) => string.Concat(Words(s)).ToLowerInvariant();

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex WordSplit();

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+$")]
    private static partial Regex NameChars();
}

public static class ToolLintExtra
{
    private static readonly HashSet<string> Destructive = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "remove", "drop", "destroy", "pay", "transfer", "send", "purchase", "buy", "revoke", "reset", "cancel",
    };

    public static bool LooksDestructive(ToolDescriptor t) => ToolLint.Words(t.Name).Any(Destructive.Contains);
}
