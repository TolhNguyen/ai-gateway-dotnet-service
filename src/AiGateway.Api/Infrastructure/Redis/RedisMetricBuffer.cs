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
        string clientCode,
        string modelCode,
        string partnerCode,
        string accountCode,
        int latencyMs,
        AiUsageDto? usage,
        bool fallbackSuccess)
    {
        var key = CurrentHourMetricKey(clientCode, modelCode, partnerCode, accountCode);

        var batch = _db.CreateBatch();
        _ = batch.HashIncrementAsync(key, "total", 1);
        _ = batch.HashIncrementAsync(key, "success", 1);
        _ = batch.HashIncrementAsync(key, "latency_total_ms", latencyMs);
        _ = batch.HashIncrementAsync(key, "latency_count", 1);

        if (fallbackSuccess)
        {
            _ = batch.HashIncrementAsync(key, "fallback_success", 1);
        }

        if (usage?.InputTokens is > 0)
        {
            _ = batch.HashIncrementAsync(key, "tokens_in", usage.InputTokens.Value);
        }

        if (usage?.OutputTokens is > 0)
        {
            _ = batch.HashIncrementAsync(key, "tokens_out", usage.OutputTokens.Value);
        }

        if (usage?.TotalTokens is > 0)
        {
            _ = batch.HashIncrementAsync(key, "tokens_total", usage.TotalTokens.Value);
        }

        _ = batch.KeyExpireAsync(key, TimeSpan.FromSeconds(RedisKeys.RuntimeRetentionSeconds));
        _ = batch.SetAddAsync(RedisKeys.MetricIndex(), key);
        batch.Execute();

        await Task.CompletedTask;
    }

    public async Task RecordErrorAsync(
        string clientCode,
        string modelCode,
        string partnerCode,
        string accountCode,
        int latencyMs,
        string errorType)
    {
        var key = CurrentHourMetricKey(clientCode, modelCode, partnerCode, accountCode);
        var safeErrorType = string.IsNullOrWhiteSpace(errorType) ? "unknown" : errorType;

        var batch = _db.CreateBatch();
        _ = batch.HashIncrementAsync(key, "total", 1);
        _ = batch.HashIncrementAsync(key, "failed", 1);
        _ = batch.HashIncrementAsync(key, $"error_{safeErrorType}", 1);
        _ = batch.HashIncrementAsync(key, "latency_total_ms", latencyMs);
        _ = batch.HashIncrementAsync(key, "latency_count", 1);
        _ = batch.KeyExpireAsync(key, TimeSpan.FromSeconds(RedisKeys.RuntimeRetentionSeconds));
        _ = batch.SetAddAsync(RedisKeys.MetricIndex(), key);
        batch.Execute();

        await Task.CompletedTask;
    }

    private static string CurrentHourMetricKey(
        string clientCode,
        string modelCode,
        string partnerCode,
        string accountCode)
    {
        var hour = DateTimeOffset.UtcNow.ToString("yyyyMMddHH");
        return RedisKeys.MetricHour(hour, Safe(clientCode), Safe(modelCode), Safe(partnerCode), Safe(accountCode));
    }

    private static string Safe(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace(':', '_');
}
