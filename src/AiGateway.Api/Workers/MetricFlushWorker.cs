using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiGateway.Api.Workers;

public sealed class MetricFlushWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MetricFlushWorker> _logger;
    private readonly int _intervalSeconds;

    public MetricFlushWorker(
        IServiceProvider serviceProvider,
        IConnectionMultiplexer redis,
        IOptions<AiGatewayOptions> options,
        ILogger<MetricFlushWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _redis = redis;
        _logger = logger;
        _intervalSeconds = Math.Max(10, options.Value.MetricFlushSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metric flush failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var keys = await db.SetMembersAsync(RedisKeys.MetricIndex());

        if (keys.Length == 0)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<AiMetricsRepository>();

        foreach (var keyValue in keys)
        {
            var key = keyValue.ToString();

            if (!await db.KeyExistsAsync(key))
            {
                await db.SetRemoveAsync(RedisKeys.MetricIndex(), key);
                continue;
            }

            var parsed = TryParseMetricKey(key);
            if (parsed is null)
            {
                await db.SetRemoveAsync(RedisKeys.MetricIndex(), key);
                continue;
            }

            var entries = await db.HashGetAllAsync(key);
            var map = entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

            var snapshot = new MetricSnapshot
            {
                BucketHour = parsed.Value.BucketHour,
                ClientCode = parsed.Value.ClientCode,
                ModelCode = parsed.Value.ModelCode,
                PartnerCode = parsed.Value.PartnerCode,
                AccountCode = parsed.Value.AccountCode,
                Total = GetLong(map, "total"),
                Success = GetLong(map, "success"),
                Failed = GetLong(map, "failed"),
                FallbackSuccess = GetLong(map, "fallback_success"),
                InputTokens = GetLong(map, "tokens_in"),
                OutputTokens = GetLong(map, "tokens_out"),
                TotalTokens = GetLong(map, "tokens_total"),
                LatencyTotalMs = GetLong(map, "latency_total_ms"),
                LatencyCount = GetLong(map, "latency_count"),
                ErrorRateLimit = GetLong(map, "error_rate_limit"),
                ErrorQuotaExceeded = GetLong(map, "error_quota_exceeded"),
                ErrorTimeout = GetLong(map, "error_timeout"),
                ErrorServerError = GetLong(map, "error_server_error"),
                ErrorAuthError = GetLong(map, "error_auth_error"),
                ErrorPermissionError = GetLong(map, "error_permission_error"),
                ErrorBadResponse = GetLong(map, "error_bad_response"),
                ErrorUnknown = GetLong(map, "error_unknown")
            };

            await repository.UpsertHourlyAsync(snapshot);
        }
    }

    private static ParsedMetricKey? TryParseMetricKey(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 12) return null;
        if (parts[0] != "ai" || parts[1] != "metric" || parts[2] != "h") return null;
        if (parts[4] != "client" || parts[6] != "model" || parts[8] != "partner" || parts[10] != "account") return null;

        if (!DateTimeOffset.TryParseExact(
                parts[3],
                "yyyyMMddHH",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var bucketHour))
        {
            return null;
        }

        return new ParsedMetricKey(
            bucketHour,
            parts[5],
            parts[7],
            parts[9],
            parts[11]);
    }

    private static long GetLong(Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value) && long.TryParse(value, out var result) ? result : 0;

    private readonly record struct ParsedMetricKey(
        DateTimeOffset BucketHour,
        string ClientCode,
        string ModelCode,
        string PartnerCode,
        string AccountCode);
}
