using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiGateway.Api.Workers;

public sealed class MetricFlushWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiGatewayOptions _opts;
    private readonly ILogger<MetricFlushWorker> _logger;

    public MetricFlushWorker(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        IOptions<AiGatewayOptions> opts,
        ILogger<MetricFlushWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm-up delay so Postgres is reachable before the first tick.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        var interval = TimeSpan.FromSeconds(_opts.MetricFlushSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metric flush iteration failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task FlushOnceAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var indexKey = RedisKeys.MetricIndex();

        var keys = await db.SetMembersAsync(indexKey);
        if (keys.Length == 0) return;

        var twoHoursAgo = DateTimeOffset.UtcNow.AddHours(-2);

        using var scope = _scopeFactory.CreateScope();
        var metricsRepo = scope.ServiceProvider.GetRequiredService<AiMetricsRepository>();

        foreach (var rk in keys)
        {
            var keyStr = rk.ToString();
            var parsed = TryParseKey(keyStr);
            if (parsed is null)
            {
                await db.SetRemoveAsync(indexKey, rk);
                continue;
            }

            var dirty = await db.HashGetAsync(keyStr, "_dirty");
            if (dirty.HasValue && dirty == "1")
            {
                var entries = await db.HashGetAllAsync(keyStr);
                if (entries.Length == 0) continue;

                var map = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);

                try
                {
                    await metricsRepo.UpsertHourlyAsync(
                        parsed.Value.BucketHour, parsed.Value.UserId,
                        parsed.Value.ModelCode, parsed.Value.PartnerCode, parsed.Value.KeyCode,
                        L(map, "total"), L(map, "success"), L(map, "failed"), L(map, "fallback_success"),
                        L(map, "tokens_in"), L(map, "tokens_out"), L(map, "tokens_total"),
                        L(map, "latency_total_ms"), L(map, "latency_count"),
                        L(map, "error_rate_limit"), L(map, "error_quota_exceeded"),
                        L(map, "error_timeout"),    L(map, "error_server_error"),
                        L(map, "error_auth_error"), L(map, "error_permission_error"),
                        L(map, "error_bad_response"), L(map, "error_unknown"),
                        ct);

                    await db.HashDeleteAsync(keyStr, "_dirty");
                    // Zero out counters so subsequent ticks only flush incremental data.
                    await db.KeyDeleteAsync(keyStr);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Flushing metric key {Key} failed", keyStr);
                    // Leave _dirty marker so it retries next tick.
                    continue;
                }
            }

            // Cleanup buckets older than 2 hours regardless of dirty state.
            if (parsed.Value.BucketHour < twoHoursAgo)
            {
                await db.KeyDeleteAsync(keyStr);
                await db.SetRemoveAsync(indexKey, keyStr);
            }
        }
    }

    private static long L(Dictionary<string, string> m, string k)
        => m.TryGetValue(k, out var v) && long.TryParse(v, out var n) ? n : 0;

    /// <summary>
    /// Parse key: ai:metric:h:YYYYMMDDHH:user:{id}:model:{m}:partner:{p}:key:{k}
    /// </summary>
    private static (DateTimeOffset BucketHour, long UserId, string ModelCode, string PartnerCode, string KeyCode)?
        TryParseKey(string key)
    {
        try
        {
            var parts = key.Split(':');
            if (parts.Length < 12) return null;
            if (parts[0] != "ai" || parts[1] != "metric" || parts[2] != "h") return null;

            var hour = DateTime.ParseExact(parts[3], "yyyyMMddHH", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
            var bucket = new DateTimeOffset(hour, TimeSpan.Zero);

            var userId = long.Parse(parts[5]);
            var modelCode = parts[7];
            var partnerCode = parts[9];
            var keyCode = parts[11];

            return (bucket, userId, modelCode, partnerCode, keyCode);
        }
        catch { return null; }
    }
}
