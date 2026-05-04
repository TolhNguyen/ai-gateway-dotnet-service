using System.Diagnostics;
using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Partners;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Infrastructure.Security;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Application;

public sealed class AiGatewayService
{
    private readonly AiConfigService _configService;
    private readonly ClientAuthService _clientAuthService;
    private readonly AiRouteSelector _routeSelector;
    private readonly PartnerClientFactory _partnerClientFactory;
    private readonly RedisRateLimitStore _rateLimitStore;
    private readonly RedisCooldownStore _cooldownStore;
    private readonly RedisMetricBuffer _metricBuffer;
    private readonly ErrorRecordingService _errorRecordingService;
    private readonly ISecretProtector _secretProtector;
    private readonly TokenEstimator _tokenEstimator;
    private readonly AiGatewayOptions _options;

    public AiGatewayService(
        AiConfigService configService,
        ClientAuthService clientAuthService,
        AiRouteSelector routeSelector,
        PartnerClientFactory partnerClientFactory,
        RedisRateLimitStore rateLimitStore,
        RedisCooldownStore cooldownStore,
        RedisMetricBuffer metricBuffer,
        ErrorRecordingService errorRecordingService,
        ISecretProtector secretProtector,
        TokenEstimator tokenEstimator,
        IOptions<AiGatewayOptions> options)
    {
        _configService = configService;
        _clientAuthService = clientAuthService;
        _routeSelector = routeSelector;
        _partnerClientFactory = partnerClientFactory;
        _rateLimitStore = rateLimitStore;
        _cooldownStore = cooldownStore;
        _metricBuffer = metricBuffer;
        _errorRecordingService = errorRecordingService;
        _secretProtector = secretProtector;
        _tokenEstimator = tokenEstimator;
        _options = options.Value;
    }

    public async Task<GenerateAiResponse> GenerateAsync(
        GenerateAiRequest request,
        string? headerClientCode,
        string? headerApiKey,
        CancellationToken cancellationToken)
    {
        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId;

        var totalStopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return Fail(requestId, request.Model, "validation_error", "Model is required", totalStopwatch.ElapsedMilliseconds);
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Fail(requestId, request.Model, "validation_error", "Prompt is required", totalStopwatch.ElapsedMilliseconds);
        }

        ClientAuthResult auth;
        try
        {
            auth = await _clientAuthService.AuthenticateAsync(request, headerClientCode, headerApiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(requestId, request.Model, "auth_error", ex.Message, totalStopwatch.ElapsedMilliseconds);
        }

        var clientCode = auth.ClientCode;

        var model = await _configService.GetModelByCodeAsync(request.Model, cancellationToken);
        if (model is null || !string.Equals(model.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(requestId, request.Model, "model_not_found", $"Model not found or inactive: {request.Model}", totalStopwatch.ElapsedMilliseconds);
        }

        var messages = BuildMessages(request);
        var partnerRequest = new PartnerGenerateRequest
        {
            Messages = messages,
            Temperature = request.Temperature ?? model.DefaultTemperature,
            MaxTokens = request.MaxTokens ?? model.DefaultMaxTokens
        };

        var reservedTokens = _tokenEstimator.EstimateReservedTokens(messages, partnerRequest.MaxTokens);
        var attemptedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var attemptErrors = new List<AiAttemptErrorDto>();
        var providerAttempt = 0;

        while (providerAttempt <= model.MaxRetry)
        {
            var candidate = await _routeSelector.SelectAsync(model, attemptedAccounts, cancellationToken);

            if (candidate is null)
            {
                break;
            }

            attemptedAccounts.Add(candidate.Account.Code);

            var reservation = await _rateLimitStore.TryReserveAccountUsageAsync(
                candidate.Account,
                model.Code,
                reservedTokens,
                cancellationToken);

            if (!reservation.Allowed)
            {
                attemptErrors.Add(new AiAttemptErrorDto
                {
                    PartnerCode = candidate.Partner.Code,
                    AccountCode = candidate.Account.Code,
                    ErrorType = "quota_exceeded",
                    ErrorMessage = $"Local quota blocked account {candidate.Account.Code}: {reservation.Reason} {reservation.Used}/{reservation.Limit}"
                });

                continue;
            }

            var currentAttempt = providerAttempt;
            providerAttempt++;

            var attemptStopwatch = Stopwatch.StartNew();
            await _rateLimitStore.IncreaseInflightAsync(candidate.Partner.Code, candidate.Account.Code);

            try
            {
                var apiKey = _secretProtector.Unprotect(candidate.Account.ApiKeyEnc!);
                var partnerClient = _partnerClientFactory.GetClient(candidate.Partner.AdapterCode);

                var partnerResult = await partnerClient.GenerateAsync(
                    new PartnerCallContext
                    {
                        PartnerCode = candidate.Partner.Code,
                        AccountCode = candidate.Account.Code,
                        ApiKey = apiKey,
                        BaseUrl = candidate.Partner.BaseUrl,
                        ProviderModel = candidate.Route.ProviderModel,
                        TimeoutMs = candidate.Route.TimeoutMs
                    },
                    partnerRequest,
                    cancellationToken);

                attemptStopwatch.Stop();

                if (partnerResult.Success)
                {
                    await _rateLimitStore.AdjustReservedTokenUsageAsync(
                        reservation,
                        partnerResult.Usage?.TotalTokens,
                        cancellationToken);

                    await _metricBuffer.RecordSuccessAsync(
                        clientCode,
                        model.Code,
                        candidate.Partner.Code,
                        candidate.Account.Code,
                        Convert.ToInt32(attemptStopwatch.ElapsedMilliseconds),
                        partnerResult.Usage,
                        fallbackSuccess: currentAttempt > 0);

                    totalStopwatch.Stop();

                    return new GenerateAiResponse
                    {
                        Success = true,
                        RequestId = requestId,
                        Model = model.Code,
                        Content = partnerResult.Content,
                        Usage = partnerResult.Usage,
                        LatencyMs = Convert.ToInt32(totalStopwatch.ElapsedMilliseconds),
                        Routing = ShouldExposeRouting(request)
                            ? new AiRoutingDto
                            {
                                PartnerCode = candidate.Partner.Code,
                                AccountCode = candidate.Account.Code,
                                RouteCode = candidate.Route.RouteCode,
                                ProviderModel = candidate.Route.ProviderModel,
                                FallbackUsed = currentAttempt > 0,
                                RetryCount = currentAttempt
                            }
                            : null
                    };
                }

                var errorType = NormalizeErrorType(partnerResult.ErrorType);
                await _errorRecordingService.RecordAsync(
                    requestId,
                    clientCode,
                    model,
                    candidate,
                    errorType,
                    partnerResult.ErrorCode,
                    partnerResult.HttpStatus,
                    partnerResult.ErrorMessage,
                    Convert.ToInt32(attemptStopwatch.ElapsedMilliseconds),
                    request,
                    cancellationToken);

                attemptErrors.Add(new AiAttemptErrorDto
                {
                    PartnerCode = candidate.Partner.Code,
                    AccountCode = candidate.Account.Code,
                    ErrorType = errorType,
                    ErrorMessage = partnerResult.ErrorMessage,
                    HttpStatus = partnerResult.HttpStatus
                });

                if (IsLimitError(errorType))
                {
                    await _cooldownStore.SetAccountModelCooldownAsync(
                        candidate.Account.Code,
                        model.Code,
                        errorType,
                        partnerResult.ErrorMessage,
                        GetCooldown(partnerResult));
                }

                var canFallback = model.FallbackEnabled && partnerResult.Retryable && currentAttempt < model.MaxRetry;
                if (!canFallback)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                attemptStopwatch.Stop();
                const string errorType = "unknown";

                await _errorRecordingService.RecordAsync(
                    requestId,
                    clientCode,
                    model,
                    candidate,
                    errorType,
                    null,
                    null,
                    ex.Message,
                    Convert.ToInt32(attemptStopwatch.ElapsedMilliseconds),
                    request,
                    cancellationToken);

                attemptErrors.Add(new AiAttemptErrorDto
                {
                    PartnerCode = candidate.Partner.Code,
                    AccountCode = candidate.Account.Code,
                    ErrorType = errorType,
                    ErrorMessage = ex.Message
                });

                if (!model.FallbackEnabled || currentAttempt >= model.MaxRetry)
                {
                    break;
                }
            }
            finally
            {
                await _rateLimitStore.DecreaseInflightAsync(candidate.Partner.Code, candidate.Account.Code);
            }
        }

        totalStopwatch.Stop();

        return new GenerateAiResponse
        {
            Success = false,
            RequestId = requestId,
            Model = model.Code,
            ErrorType = attemptErrors.LastOrDefault()?.ErrorType ?? "no_available_route",
            ErrorMessage = attemptErrors.Count == 0
                ? $"No available route/account for model {model.Code}"
                : "All available AI routes failed",
            Errors = attemptErrors,
            LatencyMs = Convert.ToInt32(totalStopwatch.ElapsedMilliseconds)
        };
    }

    private bool ShouldExposeRouting(GenerateAiRequest request)
        => request.Debug && _options.ExposeRoutingWhenDebug;

    private static IReadOnlyList<AiMessageDto> BuildMessages(GenerateAiRequest request)
    {
        var messages = new List<AiMessageDto>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new AiMessageDto { Role = "system", Content = request.SystemPrompt });
        }

        messages.Add(new AiMessageDto { Role = "user", Content = request.Prompt });
        return messages;
    }

    private static string NormalizeErrorType(string? errorType)
    {
        if (string.IsNullOrWhiteSpace(errorType)) return "unknown";
        return errorType.Trim().ToLowerInvariant() switch
        {
            "rate_limit" => "rate_limit",
            "quota_exceeded" => "quota_exceeded",
            "timeout" => "timeout",
            "server_error" => "server_error",
            "auth_error" => "auth_error",
            "permission_error" => "permission_error",
            "validation_error" => "validation_error",
            "model_not_found" => "model_not_found",
            "bad_response" => "bad_response",
            _ => "unknown"
        };
    }

    private static bool IsLimitError(string errorType)
        => errorType is "rate_limit" or "quota_exceeded";

    private static TimeSpan GetCooldown(PartnerGenerateResult partnerResult)
    {
        if (partnerResult.SuggestedCooldownSeconds is > 0)
        {
            return TimeSpan.FromSeconds(partnerResult.SuggestedCooldownSeconds.Value);
        }

        if (partnerResult.RetryAfterSeconds is > 0)
        {
            return TimeSpan.FromSeconds(partnerResult.RetryAfterSeconds.Value);
        }

        return partnerResult.LimitScope switch
        {
            "rpm" or "tpm" => TimeSpan.FromMinutes(1),
            "daily" or "rpd" or "tpd" => TimeSpan.FromDays(1),
            "monthly" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromMinutes(5)
        };
    }

    private static GenerateAiResponse Fail(
        string requestId,
        string? model,
        string errorType,
        string errorMessage,
        long latencyMs)
    {
        return new GenerateAiResponse
        {
            Success = false,
            RequestId = requestId,
            Model = model ?? string.Empty,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            LatencyMs = Convert.ToInt32(latencyMs)
        };
    }
}
