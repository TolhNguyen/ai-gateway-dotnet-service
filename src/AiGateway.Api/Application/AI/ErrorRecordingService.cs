using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiGateway.Api.Application.AI;

public sealed class ErrorRecordingService
{
    private readonly AiErrorRepository _repo;
    private readonly IDatabase _redis;
    private readonly AiGatewayOptions _opts;
    private readonly ILogger<ErrorRecordingService> _logger;

    public ErrorRecordingService(
        AiErrorRepository repo,
        IConnectionMultiplexer redis,
        IOptions<AiGatewayOptions> opts,
        ILogger<ErrorRecordingService> logger)
    {
        _repo = repo;
        _redis = redis.GetDatabase();
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task RecordAsync(
        string? requestId, long userId, string modelCode, string partnerCode, string accountKeyCode, long keyId,
        string errorType, string? errorCode, int? httpStatus, string? message, int latencyMs,
        CancellationToken ct)
    {
        try
        {
            await _repo.UpsertAggregateAsync(
                userId, modelCode, partnerCode, accountKeyCode,
                errorType, errorCode ?? "none", httpStatus ?? 0, message, ct);

            // Sample raw events using a Redis per-(hour, key, type) counter to cap volume.
            var hour = DateTimeOffset.UtcNow.ToString("yyyyMMddHH");
            var capKey = RedisKeys.ErrorEventCap(hour, keyId, errorType);
            var cur = (long)await _redis.StringIncrementAsync(capKey);
            if (cur == 1) await _redis.KeyExpireAsync(capKey, TimeSpan.FromHours(2));

            if (cur <= _opts.ErrorEventsMaxPerKeyTypeHour)
            {
                await _repo.InsertEventAsync(
                    requestId, userId, modelCode, partnerCode, accountKeyCode,
                    errorType, errorCode, httpStatus, message, latencyMs, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record AI error");
        }
    }
}
