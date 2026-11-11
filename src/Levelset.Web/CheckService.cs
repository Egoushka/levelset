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
    private readonly bool _allowPrivate = config.GetValue("Levelset:AllowPrivate", false);

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
            cache.Set(key, report, TimeSpan.FromMinutes(10));
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
