using System.Text.Json;
using System.Text.Json.Nodes;
using Levelset;
using Levelset.Browser;
using Levelset.Web;

var app = Levelset.Web.Site.Build(args);
app.Run();

namespace Levelset.Web
{
    public static class Site
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static WebApplication Build(string[] args)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory,
                WebRootPath = "wwwroot",
            });
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<IFetcher>(sp => new GuardedFetcher(new FetcherOptions
            {
                AllowPrivate = sp.GetRequiredService<IConfiguration>().GetValue("Levelset:AllowPrivate", false),
            }));
            builder.Services.AddSingleton<IBrowserProbe>(_ => new PlaywrightBrowserProbe());
            builder.Services.AddSingleton(sp => new Scanner(sp.GetRequiredService<IFetcher>(), sp.GetRequiredService<IBrowserProbe>()));
            builder.Services.AddSingleton<CheckService>();
            var app = builder.Build();

            app.UseMiddleware<SiteMiddleware>();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/v1/check", async (string? url, HttpContext ctx, CheckService svc, CancellationToken ct) =>
            {
                try { return Results.Json(await svc.CheckAsync(url, Client(ctx), ct), Json); }
                catch (CheckException e) { return Results.Json(new { error = e.Message }, Json, statusCode: e.Status); }
            });
            app.MapGet("/robots.txt", (HttpContext c) => Results.Text(
                $"User-agent: *\nAllow: /\n\nUser-agent: GPTBot\nAllow: /\n\nContent-Signal: search=yes, ai-input=yes, ai-train=no\n\nSitemap: {Origin(c)}/sitemap.xml\n",
                "text/plain"));
            app.MapGet("/sitemap.xml", (HttpContext c) => Results.Text(
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>{Origin(c)}/</loc></url><url><loc>{Origin(c)}/about</loc></url></urlset>\n",
                "application/xml"));
            app.MapGet("/.well-known/mcp/server-card.json", (HttpContext c) => Results.Json(new
            {
                name = "levelset",
                description = "Checks how ready a website is for AI agents.",
                remotes = new[] { new { type = "streamable-http", url = Origin(c) + "/mcp" } },
                tools = new[] { "check_site" },
            }, Json));
            app.MapPost("/mcp", async (HttpContext c, CheckService svc, CancellationToken ct) => await Mcp.HandleAsync(c, svc, ct));
            app.MapGet("/mcp", (HttpContext c) => { c.Response.Headers.Allow = "POST"; return Results.StatusCode(405); });
            return app;
        }

        public static string Origin(HttpContext c) => $"{c.Request.Scheme}://{c.Request.Host}";

        public static string Client(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>Content negotiation and discovery headers for the human pages.</summary>
    public sealed class SiteMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        public async Task InvokeAsync(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value ?? "/";
            if (path is "/" or "/about" or "/index.html")
            {
                ctx.Response.Headers.Vary = "Accept";
                ctx.Response.Headers.Append("Link", "</llms.txt>; rel=\"llms-txt\"");
                if (ctx.Request.Headers.Accept.ToString().Contains("text/markdown", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.ContentType = "text/markdown; charset=utf-8";
                    await ctx.Response.SendFileAsync(env.WebRootFileProvider.GetFileInfo(path == "/about" ? "about.md" : "index.md"));
                    return;
                }

                if (path == "/about")
                {
                    ctx.Request.Path = "/about.html";
                }
            }

            await next(ctx);
        }
    }

    public static class Mcp
    {
        private static readonly string[] Versions = ["2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25"];

        public static async Task<IResult> HandleAsync(HttpContext c, CheckService svc, CancellationToken ct)
        {
            if (c.Request.Headers.Origin.FirstOrDefault() is { } origin
                && !(Uri.TryCreate(origin, UriKind.Absolute, out var o) && o.Authority == c.Request.Host.Value))
                return Results.StatusCode(403);

            JsonNode? msg;
            try { msg = await JsonNode.ParseAsync(c.Request.Body, cancellationToken: ct); }
            catch (JsonException) { return Reply(null, error: (-32700, "parse error")); }
            if (msg is not JsonObject req) return Reply(null, error: (-32600, "invalid request"));

            var id = req["id"]?.DeepClone();
            switch (req["method"]?.ToString())
            {
                case "initialize":
                    var asked = req["params"]?["protocolVersion"]?.ToString();
                    return Reply(id, new JsonObject
                    {
                        ["protocolVersion"] = Versions.Contains(asked) ? asked : "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "levelset", ["version"] = "0.1.0" },
                    });
                case "tools/list":
                    return Reply(id, new JsonObject { ["tools"] = new JsonArray(Tool()) });
                case "tools/call":
                    if (req["params"]?["name"]?.ToString() != "check_site") return Reply(id, error: (-32602, "unknown tool"));
                    try
                    {
                        var report = await svc.CheckAsync(req["params"]?["arguments"]?["url"]?.ToString(), Site.Client(c), ct);
                        var structured = JsonSerializer.SerializeToNode(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        return Reply(id, new JsonObject
                        {
                            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = structured!.ToJsonString() }),
                            ["structuredContent"] = structured,
                        });
                    }
                    catch (CheckException e)
                    {
                        return Reply(id, new JsonObject
                        {
                            ["isError"] = true,
                            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = e.Message }),
                        });
                    }
                default:
                    return id is null ? Results.StatusCode(202) : Reply(id, error: (-32601, "method not found"));
            }
        }

        private static IResult Reply(JsonNode? id, JsonNode? result = null, (int Code, string Message)? error = null)
        {
            var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
            if (error is { } e) body["error"] = new JsonObject { ["code"] = e.Code, ["message"] = e.Message };
            else body["result"] = result;
            return Results.Content(body.ToJsonString(), "application/json");
        }

        private static JsonObject Tool() => new()
        {
            ["name"] = "check_site",
            ["description"] = "Scan a public website and return its agent-readiness score out of 100 with findings and a fix for each. Use it before shipping a site that agents will read or call.",
            ["inputSchema"] = JsonNode.Parse("""{"type":"object","properties":{"url":{"type":"string","description":"The http or https address of the site to check"}},"required":["url"]}"""),
            ["outputSchema"] = JsonNode.Parse("""{"type":"object","properties":{"score":{"type":"integer","description":"Score from 0 to 100"},"findings":{"type":"array","description":"What to fix, each with an ID, severity and fix hint"}}}"""),
            ["annotations"] = JsonNode.Parse("""{"readOnlyHint":true,"destructiveHint":false,"idempotentHint":true,"openWorldHint":true}"""),
        };
    }
}
