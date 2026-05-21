using AiGateway.Api.Contracts;
using StackExchange.Redis;

namespace AiGateway.Api.Infrastructure.Redis;

public sealed class RedisMetricBuffer
{
    private readonly IDatabase _db;

    public RedisMetricBuffer(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task RecordSuccessAsync(
        long userId, string modelCode, string partnerCode, string accountKeyCode,
        int latencyMs, AiUsageDto? usage, bool fallbackSuccess)
    {
        var key = HourKey(userId, modelCode, partnerCode, accountKeyCode);

        var batch = _db.CreateBatch();
        _ = batch.HashIncrementAsync(key, "total", 1);
        _ = batch.HashIncrementAsync(key, "success", 1);
        _ = batch.HashIncrementAsync(key, "latency_total_ms", latencyMs);
        _ = batch.HashIncrementAsync(key, "latency_count", 1);
        if (fallbackSuccess) _ = batch.HashIncrementAsync(key, "fallback_success", 1);

        if (usage?.InputTokens  is > 0) _ = batch.HashIncrementAsync(key, "tokens_in",   usage.InputTokens.Value);
        if (usage?.OutputTokens is > 0) _ = batch.HashIncrementAsync(key, "tokens_out",  usage.OutputTokens.Value);
        if (usage?.TotalTokens  is > 0) _ = batch.HashIncrementAsync(key, "tokens_total", usage.TotalTokens.Value);

        _ = batch.HashSetAsync(key, "_dirty", "1");
        _ = batch.KeyExpireAsync(key, TimeSpan.FromSeconds(RedisKeys.RuntimeRetentionSeconds));
        _ = batch.SetAddAsync(RedisKeys.MetricIndex(), key);
        batch.Execute();
        await Task.CompletedTask;
    }

    public async Task RecordErrorAsync(
        long userId, string modelCode, string partnerCode, string accountKeyCode,
        int latencyMs, string errorType)
    {
        var key = HourKey(userId, modelCode, partnerCode, accountKeyCode);
        var safeType = string.IsNullOrWhiteSpace(errorType) ? "unknown" : errorType;

        var batch = _db.CreateBatch();
        _ = batch.HashIncrementAsync(key, "total", 1);
        _ = batch.HashIncrementAsync(key, "failed", 1);
        _ = batch.HashIncrementAsync(key, $"error_{safeType}", 1);
        _ = batch.HashIncrementAsync(key, "latency_total_ms", latencyMs);
        _ = batch.HashIncrementAsync(key, "latency_count", 1);
        _ = batch.HashSetAsync(key, "_dirty", "1");
        _ = batch.KeyExpireAsync(key, TimeSpan.FromSeconds(RedisKeys.RuntimeRetentionSeconds));
        _ = batch.SetAddAsync(RedisKeys.MetricIndex(), key);
        batch.Execute();
        await Task.CompletedTask;
    }

    private static string HourKey(long userId, string m, string p, string k)
    {
        var h = DateTimeOffset.UtcNow.ToString("yyyyMMddHH");
        return RedisKeys.MetricHour(h, userId, Safe(m), Safe(p), Safe(k));
    }

    private static string Safe(string v)
        => string.IsNullOrWhiteSpace(v) ? "unknown" : v.Replace(':', '_');
}
