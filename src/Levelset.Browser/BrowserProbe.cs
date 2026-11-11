using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Levelset.Browser;

public sealed class BrowserOptions
{
    /// <summary>A Chrome or Chromium binary; defaults to LEVELSET_CHROME_PATH, else Playwright's own download.</summary>
    public string? ExecutablePath { get; init; } = Environment.GetEnvironmentVariable("LEVELSET_CHROME_PATH");

    /// <summary>"chrome" uses the installed stable Chrome; defaults to LEVELSET_CHROME_CHANNEL.</summary>
    public string? Channel { get; init; } = Environment.GetEnvironmentVariable("LEVELSET_CHROME_CHANNEL");

    public int PageBudgetSeconds { get; init; } = 15;
    public int SettleMilliseconds { get; init; } = 3000;
}

/// <summary>
/// Loads a page in headless Chrome with the recorder shim injected first. Nothing on the page is clicked, typed
/// into or submitted, and no tool is executed. One browser per probe, closed after use.
/// </summary>
public sealed class PlaywrightBrowserProbe(BrowserOptions? options = null) : IBrowserProbe
{
    private static readonly string Shim = Resource("recorder.js");
    private static readonly string Collect = Resource("collect.js");
    private readonly BrowserOptions _options = options ?? new BrowserOptions();

    public async Task<BrowserResult> ProbeAsync(Uri url, bool allowPrivate, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Channel = _options.Channel,
                ExecutablePath = _options.ExecutablePath,
                Args = ["--no-sandbox", "--disable-dev-shm-usage"],
            });
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                AcceptDownloads = false,
                ServiceWorkers = ServiceWorkerPolicy.Block,
                UserAgent = "Levelset/1 (+https://levelset.hrabovskyi.online/about)",
            });
            await context.AddInitScriptAsync(Shim);
            await context.RouteAsync("**/*", async route =>
            {
                if (await Allowed(route.Request.Url, allowPrivate)) await route.ContinueAsync();
                else await route.AbortAsync("blockedbyclient");
            });
            var page = await context.NewPageAsync();
            page.Dialog += (_, d) => _ = d.DismissAsync();
            page.Popup += (_, popup) => _ = popup.CloseAsync();
            var budget = _options.PageBudgetSeconds * 1000;
            await page.GotoAsync(url.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = budget });
            try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5000 }); }
            catch (TimeoutException) { /* a page that never idles is still read */ }
            await Task.Delay(_options.SettleMilliseconds, ct);
            var json = await page.EvaluateAsync<string>(Collect);
            return Parse(json, browser.Version);
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            return new BrowserResult("none", null, [], false, 0, FirstLine(e.Message));
        }
    }

    private static async Task<bool> Allowed(string raw, bool allowPrivate)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u)) return false;
        if (u.Scheme is "data" or "blob" or "about") return true;
        if (!UrlGuard.IsWebUrl(u)) return false;
        if (allowPrivate) return true;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(u.Host);
            return addresses.Length > 0 && addresses.All(UrlGuard.IsPublic);
        }
        catch (System.Net.Sockets.SocketException) { return false; }
    }

    internal static BrowserResult Parse(string json, string chrome)
    {
        var root = JsonNode.Parse(json)!;
        var tools = new List<ToolDescriptor>();
        foreach (var t in root["tools"]!.AsArray())
            tools.Add(new ToolDescriptor("imperative", t!["name"]?.ToString(), t["description"]?.ToString(), t["inputSchema"]?.DeepClone(),
                t["outputSchema"]?.DeepClone(), t["annotations"] as JsonObject, t["hasExecute"]?.GetValue<bool>() ?? true,
                t["at"]?.GetValue<int>() ?? 0));
        foreach (var f in root["forms"]!.AsArray())
            tools.Add(FormTool(f!));
        return new BrowserResult(root["mode"]!.ToString(), chrome, tools, root["navigatorOnly"]!.GetValue<bool>(),
            root["textLength"]!.GetValue<int>(), null);
    }

    private static ToolDescriptor FormTool(JsonNode f)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var field in f["fields"]!.AsArray())
        {
            var type = field!["type"]?.ToString();
            var p = new JsonObject
            {
                ["type"] = type is "number" or "range" ? "number" : type == "checkbox" ? "boolean" : "string",
            };
            if (field["description"]?.ToString() is { Length: > 0 } d) p["description"] = d;
            props[field["name"]!.ToString()] = p;
            if (field["required"]!.GetValue<bool>()) required.Add(field["name"]!.ToString());
        }

        var schema = new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = required };
        return new ToolDescriptor("declarative", f["name"]?.ToString(), f["description"]?.ToString(), schema, null, null,
            HasExecute: true, AutoSubmit: f["autoSubmit"]!.GetValue<bool>(), Method: f["method"]?.ToString());
    }

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();

    private static string Resource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var full = asm.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal));
        using var reader = new StreamReader(asm.GetManifestResourceStream(full)!);
        return reader.ReadToEnd();
    }
}
