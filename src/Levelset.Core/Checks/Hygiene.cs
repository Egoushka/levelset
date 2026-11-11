using System.Text.RegularExpressions;

namespace Levelset;

public static partial class Catalog
{
    private static IEnumerable<Check> HygieneChecks()
    {
        const Category H = Category.Hygiene;
        yield return Def("HYG-001", H, Severity.Error, CheckStatus.Standard, "HTTPS is valid and the page has no mixed content",
            "https://developer.mozilla.org/docs/Web/Security/Mixed_content",
            "Serve the site over HTTPS with a valid certificate and load every script, style and frame over https. WebMCP only works in secure contexts.",
            s =>
            {
                if (s.Options.AllowPrivate && UrlGuard.IsLocalHost(s.Url)) return null;
                var h = s.Home;
                if (h is null || h.Error is not null) return One("/", "could not connect: " + (h?.Error ?? "no response"));
                if (s.Url.Scheme != "https" && h.Url.Scheme != "https") return One("/", "the site is served over http");
                var mixed = MixedContent().Matches(h.Body).Select(m => m.Groups[1].Value).Distinct().Take(5).ToList();
                return mixed.Count == 0 ? Pass : One("/", "loads over http: " + string.Join(", ", mixed));
            });
        yield return Def("HYG-002", H, Severity.Warning, CheckStatus.Standard, "A plain agent fetch is not stopped by a bot challenge",
            "https://developers.cloudflare.com/bots/",
            "Let identified agents through: allow-list known agent user agents or signed agents in your bot protection, or serve /llms.txt and the MCP endpoint outside the challenge.",
            s =>
            {
                var h = s.Home;
                if (h is null || h.Status is not (403 or 503)) return Pass;
                var challenge = h.Header("cf-mitigated") == "challenge"
                    || Regex.IsMatch(h.Body, "Just a moment|cf-chl|captcha|Attention Required", RegexOptions.IgnoreCase);
                return challenge ? One("/", $"status {h.Status} with a bot challenge") : Pass;
            });
        yield return Def("HYG-004", H, Severity.Warning, CheckStatus.Standard, "The main document answers in under 3 seconds",
            "https://web.dev/articles/ttfb",
            "Agents time out quickly: cache the page or shorten the slowest dependency on the first request.",
            s =>
            {
                var problems = new List<Problem>();
                if (s.Home is { Error: null, ElapsedMs: > 3000 } h) problems.Add(new("/", $"main document took {h.ElapsedMs} ms"));
                if (s.Mcp?.ToolsList is { ElapsedMs: > 3000 } t) problems.Add(new("/mcp", $"tools/list took {t.ElapsedMs} ms"));
                return problems;
            });
    }


    [GeneratedRegex(@"<(?:script|img|iframe|source|video|audio|embed)\b[^>]*\bsrc=[""'](http://[^""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex MixedContent();
}
