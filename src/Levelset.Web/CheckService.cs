using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Levelset.Web;

public sealed class CheckException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>The hosted entry to the engine: URL validation, a daily limit per client, a 10 minute cache and at most two browsers at once.</summary>
public sealed class CheckService(Scanner scanner, IMemoryCache cache, IConfiguration config)
{
    private readonly SemaphoreSlim _gate = new(config.GetValue("Levelset:MaxConcurrent", 2));
    private readonly ConcurrentDictionary<string, int> _used = new();
    private readonly int _dailyLimit = config.GetValue("Levelset:DailyLimit", 10);
    private readonly ConcurrentDictionary<string, Report> _last = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(config.GetValue("Levelset:CacheSeconds", 600));
    private readonly bool _allowPrivate = config.GetValue("Levelset:AllowPrivate", false);

    /// <summary>
    /// A badge read: a fresh scan when the client may still scan, else the last score seen for the address.
    /// Image proxies share one client for every reader, so a badge must not depend on that client's daily limit.
    /// </summary>
    public async Task<Report?> BadgeAsync(string? raw, string client, CancellationToken ct)
    {
        try { return await CheckAsync(raw, client, ct); }
        catch (CheckException e) when (e.Status is 429 or 504 && Uri.TryCreate(raw, UriKind.Absolute, out var u) && _last.TryGetValue(u.ToString(), out var last))
        {
            return last;
        }
    }

    public async Task<Report> CheckAsync(string? raw, string client, CancellationToken ct)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || !UrlGuard.IsWebUrl(url))
            throw new CheckException(400, "url must be an absolute http or https address");
        if (!_allowPrivate && UrlGuard.IsLocalHost(url))
            throw new CheckException(400, "private and local addresses are not scanned by the hosted service; run the GitHub Action in your own CI");

        var key = url.ToString();
        if (cache.TryGetValue(key, out Report? hit) && hit is not null) return hit;

        if (_dailyLimit > 0)
        {
            var day = $"{client}:{DateTime.UtcNow:yyyyMMdd}";
            if (_used.AddOrUpdate(day, 1, (_, n) => n + 1) > _dailyLimit)
                throw new CheckException(429, $"the free check allows {_dailyLimit} scans a day per address");
        }

        await _gate.WaitAsync(ct);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(60));
            var report = await scanner.ScanAsync(url, new ScanOptions { AllowPrivate = _allowPrivate }, budget.Token);
            if (_ttl > TimeSpan.Zero) cache.Set(key, report, _ttl);
            if (_last.Count >= 5000) _last.Clear();
            _last[key] = report;
            return report;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CheckException(504, "the scan ran out of time");
        }
        finally
        {
            _gate.Release();
        }
    }
}
