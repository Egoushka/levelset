using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Levelset;

public sealed record FetchResult(
    Uri Url,
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    long ElapsedMs,
    string? Error)
{
    public bool Ok => Error is null && Status is >= 200 and < 300;

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

    public string ContentType => (Header("content-type") ?? "").ToLowerInvariant();

    public static FetchResult Failed(Uri url, string error, long elapsedMs = 0) =>
        new(url, 0, new Dictionary<string, string>(), "", elapsedMs, error);
}

/// <summary>The only way the engine talks to a site. Implementations must stay GET/POST of bytes: no scripts.</summary>
public interface IFetcher
{
    Task<FetchResult> SendAsync(HttpMethod method, Uri url, IReadOnlyDictionary<string, string>? headers,
        string? body, CancellationToken ct);
}

public static class FetcherExtensions
{
    public static Task<FetchResult> GetAsync(this IFetcher f, Uri url, CancellationToken ct,
        IReadOnlyDictionary<string, string>? headers = null) =>
        f.SendAsync(HttpMethod.Get, url, headers, null, ct);
}

public static class UrlGuard
{
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6UniqueLocal) return false;
            return true;
        }

        var b = ip.GetAddressBytes();
        return !(b[0] == 10
            || b[0] == 127
            || b[0] == 0
            || (b[0] == 100 && b[1] is >= 64 and <= 127) // CGNAT, includes the tailnet 100.64.0.0/10
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)
            || (b[0] == 198 && b[1] is 18 or 19)
            || b[0] >= 224);
    }

    public static bool IsWebUrl(Uri url) => url.Scheme is "http" or "https";

    /// <summary>True when the host is a loopback or private literal or a localhost name.</summary>
    public static bool IsLocalHost(Uri url) =>
        url.IsLoopback
        || url.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(url.Host.Trim('[', ']'), out var ip) && !IsPublic(ip));
}

public sealed class FetcherOptions
{
    public bool AllowPrivate { get; init; }
    public int TimeoutSeconds { get; init; } = 15;
    public int MaxBodyBytes { get; init; } = 5 * 1024 * 1024;
    public string UserAgent { get; init; } = "Levelset/1 (+https://levelset.hrabovskyi.online/about)";
}

/// <summary>
/// HttpClient whose connect step resolves the name itself, refuses non-public addresses and connects to the
/// resolved address. The check runs on every connection, so redirects are covered.
/// </summary>
public sealed class GuardedFetcher : IFetcher, IDisposable
{
    private readonly HttpClient _http;
    private readonly FetcherOptions _options;

    public GuardedFetcher(FetcherOptions? options = null)
    {
        _options = options ?? new FetcherOptions();
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseProxy = false,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            ConnectCallback = ConnectAsync,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds) };
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
        var allowed = addresses.Where(a => _options.AllowPrivate || UrlGuard.IsPublic(a)).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException($"blocked: {ctx.DnsEndPoint.Host} resolves to a non-public address");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed[0], ctx.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<FetchResult> SendAsync(HttpMethod method, Uri url,
        IReadOnlyDictionary<string, string>? headers, string? body, CancellationToken ct)
    {
        if (!UrlGuard.IsWebUrl(url)) return FetchResult.Failed(url, "blocked: only http and https are fetched");
        var started = Environment.TickCount64;
        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            foreach (var (k, v) in headers ?? new Dictionary<string, string>())
            {
                request.Headers.TryAddWithoutValidation(k, v);
            }

            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var text = await ReadCappedAsync(response, ct);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in response.Headers.Concat(response.Content.Headers))
                map[h.Key] = string.Join(", ", h.Value);
            return new FetchResult(response.RequestMessage?.RequestUri ?? url, (int)response.StatusCode, map, text,
                Environment.TickCount64 - started, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException
                                      or InvalidOperationException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return FetchResult.Failed(url, e.InnerException?.Message ?? e.Message, Environment.TickCount64 - started);
        }
    }

    private async Task<string> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length >= _options.MaxBodyBytes) break;
        }

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    public void Dispose() => _http.Dispose();
}
