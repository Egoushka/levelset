using System.Text.Json.Nodes;

namespace Levelset.Tests;

public sealed class FakeFetcher : IFetcher
{
    public Dictionary<string, Func<HttpMethod, IReadOnlyDictionary<string, string>?, string?, FetchResult>> Routes { get; } = new();

    public static FetchResult Resp(string url, int status, string body, string contentType = "text/plain", params (string, string)[] headers)
    {
        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["content-type"] = contentType };
        foreach (var (k, v) in headers) h[k] = v;
        return new FetchResult(new Uri(url), status, h, body, 5, null);
    }

    public FakeFetcher Add(string url, int status, string body, string contentType = "text/plain", params (string, string)[] headers)
    {
        Routes[url] = (_, _, _) => Resp(url, status, body, contentType, headers);
        return this;
    }

    public Task<FetchResult> SendAsync(HttpMethod method, Uri url, IReadOnlyDictionary<string, string>? headers, string? body, CancellationToken ct) =>
        Task.FromResult(Routes.TryGetValue(url.ToString(), out var r) ? r(method, headers, body) : Resp(url.ToString(), 404, "not found"));
}

public static class Fx
{
    public static ToolDescriptor Tool(string name, string description, JsonNode? schema = null, string? annotations = null,
        string source = "imperative", int at = 0, bool autoSubmit = false, string? method = null) =>
        new(source, name, description,
            schema ?? JsonNode.Parse("""{"type":"object","properties":{"id":{"type":"string","description":"the id"}},"required":["id"]}"""),
            JsonNode.Parse("""{"type":"object"}"""), annotations is null ? null : JsonNode.Parse(annotations)!.AsObject(),
            true, at, autoSubmit, method);

    public static Snapshot Snap(Action<Snapshot>? setup = null)
    {
        var s = new Snapshot { Url = new Uri("https://example.test/"), Options = new ScanOptions() };
        s.Home = FakeFetcher.Resp("https://example.test/", 200, "<html></html>", "text/html");
        setup?.Invoke(s);
        return s;
    }

    public static Report Eval(Snapshot s) => Scanner.Evaluate(s, 0);

    public static BrowserResult Page(params ToolDescriptor[] tools) => new("shim", "test", tools, false, 0, null);
}
