using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiGateway.Api.Application;

public sealed class ErrorRecordingService
{
    private readonly RedisMetricBuffer _metricBuffer;
    private readonly AiErrorRepository _errorRepository;
    private readonly IDatabase _redis;
    private readonly AiGatewayOptions _options;

    public ErrorRecordingService(
        RedisMetricBuffer metricBuffer,
        AiErrorRepository errorRepository,
        IConnectionMultiplexer redis,
        IOptions<AiGatewayOptions> options)
    {
        _metricBuffer = metricBuffer;
        _errorRepository = errorRepository;
        _redis = redis.GetDatabase();
        _options = options.Value;
    }

    public async Task RecordAsync(
        string requestId,
        string clientCode,
        AiModelConfig model,
        AiRouteCandidate candidate,
        string errorType,
        string? errorCode,
        int? httpStatus,
        string? message,
        int latencyMs,
        GenerateAiRequest request,
        CancellationToken cancellationToken)
    {
        await _metricBuffer.RecordErrorAsync(
            clientCode,
            model.Code,
            candidate.Partner.Code,
            candidate.Account.Code,
            latencyMs,
            errorType);

        var writeEvent = await ShouldWriteErrorEventAsync(candidate.Account.Code, errorType);

        await _errorRepository.RecordErrorAsync(
            requestId,
            clientCode,
            model.Code,
            candidate.Partner.Code,
            candidate.Account.Code,
            errorType,
            errorCode,
            httpStatus,
            message,
            latencyMs,
            new
            {
                request.FeatureCode,
                request.Metadata
            },
            writeEvent,
            cancellationToken);
    }

    private async Task<bool> ShouldWriteErrorEventAsync(string accountCode, string errorType)
    {
        if (_options.ErrorEventsMaxPerAccountTypeHour <= 0)
        {
            return false;
        }

        // Keep all rare/high-signal errors; cap noisy quota/rate-limit floods.
        if (errorType is not "rate_limit" and not "quota_exceeded")
        {
            return true;
        }

        var hour = DateTimeOffset.UtcNow.ToString("yyyyMMddHH");
        var key = RedisKeys.ErrorEventCap(hour, accountCode, errorType);
        var count = await _redis.StringIncrementAsync(key);
        await _redis.KeyExpireAsync(key, TimeSpan.FromHours(2));

        return count <= _options.ErrorEventsMaxPerAccountTypeHour;
    }
}
