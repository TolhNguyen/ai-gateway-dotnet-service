using System.Diagnostics;
using AiGateway.Api.Application.Config;
using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Http;
using AiGateway.Api.Infrastructure.Partners;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Infrastructure.Security;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Application.AI;

public sealed class AiGatewayService
{
    private const string CavemanInstruction = """
    Respond in Caveman engineering style.
    Use few words.
    Keep technical substance.
    No filler.
    Prefer: cause, fix, risk, test.
    Do not remove important warnings.
    """;

    private readonly AiConfigService _config;
    private readonly AiRouteSelector _routeSelector;
    private readonly RedisRateLimitStore _rateStore;
    private readonly RedisCooldownStore _cooldownStore;
    private readonly RedisMetricBuffer _metricBuffer;
    private readonly ErrorRecordingService _errorRecorder;
    private readonly PartnerClientFactory _partnerFactory;
    private readonly TokenEstimator _tokenEstimator;
    private readonly ISecretProtector _protector;
    private readonly AccountKeyRepository _keyRepo;
    private readonly AiGatewayOptions _opts;
    private readonly ILogger<AiGatewayService> _logger;

    public AiGatewayService(
        AiConfigService config,
        AiRouteSelector routeSelector,
        RedisRateLimitStore rateStore,
        RedisCooldownStore cooldownStore,
        RedisMetricBuffer metricBuffer,
        ErrorRecordingService errorRecorder,
        PartnerClientFactory partnerFactory,
        TokenEstimator tokenEstimator,
        ISecretProtector protector,
        AccountKeyRepository keyRepo,
        IOptions<AiGatewayOptions> opts,
        ILogger<AiGatewayService> logger)
    {
        _config = config;
        _routeSelector = routeSelector;
        _rateStore = rateStore;
        _cooldownStore = cooldownStore;
        _metricBuffer = metricBuffer;
        _errorRecorder = errorRecorder;
        _partnerFactory = partnerFactory;
        _tokenEstimator = tokenEstimator;
        _protector = protector;
        _keyRepo = keyRepo;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<GenerateAiResponse> GenerateAsync(long userId, GenerateAiRequest req, HttpContext? http, CancellationToken ct)
    {
        var requestId = req.RequestId ?? (http?.Items[CorrelationIdMiddleware.HeaderName] as string)
                        ?? Guid.NewGuid().ToString("N");

        var stopwatch = Stopwatch.StartNew();

        string? resolvedModelCode = req.Model;
        if (string.IsNullOrWhiteSpace(resolvedModelCode))
        {
            var keys = await _keyRepo.ListForUserAsync(userId, ct);
            var bestKey = keys
                .Where(k => k.Status == "active" && !string.IsNullOrWhiteSpace(k.DefaultModelCode))
                .OrderBy(k => k.Priority)
                .ThenBy(k => k.Code)
                .FirstOrDefault();

            if (bestKey is null)
            {
                return Fail(requestId, string.Empty, stopwatch, "bad_response", "No model specified and no default model configured.");
            }

            resolvedModelCode = bestKey.DefaultModelCode;
        }

        // Resolve model
        var model = await _config.GetModelAsync(resolvedModelCode!, ct);
        if (model is null || model.Status != "active")
        {
            return Fail(requestId, resolvedModelCode ?? string.Empty, stopwatch, "bad_response", $"Model '{resolvedModelCode}' not found or disabled.");
        }

        // Pick candidates available for this user
        var candidates = await _routeSelector.GetCandidatesAsync(userId, model, ct);
        if (candidates.Count == 0)
        {
            return Fail(requestId, model.Code, stopwatch, "permission_error",
                "No active partner key found in your account for this model. Add an API key under My Keys.");
        }

        var temperature = req.Temperature ?? model.DefaultTemperature;
        var maxTokens   = req.MaxTokens ?? model.DefaultMaxTokens;
        var reservedOutput = Math.Min(_opts.DefaultReservedOutputTokens, maxTokens);
        var effectiveSystemPrompt = BuildEffectiveSystemPrompt(req.SystemPrompt, http);

        var inputEstimate = _tokenEstimator.EstimateInputTokens(effectiveSystemPrompt, req.Prompt);
        var reservedTotal = inputEstimate + reservedOutput;

        var attemptErrors = new List<AiAttemptErrorDto>();
        var retryCount = 0;
        var maxAttempts = Math.Min(candidates.Count, Math.Max(1, model.MaxRetry + 1));

        for (var i = 0; i < maxAttempts; i++)
        {
            var c = candidates[i];

            // Per-key + token quota reservation (atomic)
            var reservation = await _rateStore.TryReserveAccountUsageAsync(c.Key, model.Code, reservedTotal, ct);
            if (!reservation.Allowed)
            {
                attemptErrors.Add(new AiAttemptErrorDto {
                    PartnerCode = c.Partner.Code, AccountKeyCode = c.Key.Code,
                    ErrorType = $"local_{reservation.Reason}", ErrorMessage = $"reserved usage exceeds {reservation.Reason} limit"
                });
                continue;
            }

            await _rateStore.IncreaseInflightAsync(c.Partner.Code, c.Key.Id);

            try
            {
                var attemptResult = await CallPartnerAsync(c, req, effectiveSystemPrompt, temperature, maxTokens, ct);

                if (attemptResult.Success)
                {
                    var latencyMs = (int)stopwatch.ElapsedMilliseconds;
                    var fallback = i > 0;

                    await _rateStore.AdjustReservedTokenUsageAsync(reservation, attemptResult.Usage?.TotalTokens, ct);
                    await _metricBuffer.RecordSuccessAsync(
                        userId, model.Code, c.Partner.Code, c.Key.Code,
                        latencyMs, attemptResult.Usage, fallback);

                    return new GenerateAiResponse
                    {
                        Success = true,
                        RequestId = requestId,
                        Model = model.Code,
                        Content = attemptResult.Content,
                        Usage = attemptResult.Usage,
                        LatencyMs = latencyMs,
                        Errors = attemptErrors.Count > 0 ? attemptErrors : null,
                        Routing = ShouldExposeRouting(req)
                            ? new AiRoutingDto {
                                PartnerCode = c.Partner.Code, AccountKeyCode = c.Key.Code,
                                RouteCode = c.Route.RouteCode, ProviderModel = c.Route.ProviderModel,
                                FallbackUsed = fallback, RetryCount = retryCount
                            }
                            : null
                    };
                }

                // Failed attempt
                attemptErrors.Add(new AiAttemptErrorDto {
                    PartnerCode = c.Partner.Code, AccountKeyCode = c.Key.Code,
                    ErrorType = attemptResult.ErrorType, ErrorMessage = attemptResult.ErrorMessage,
                    HttpStatus = attemptResult.HttpStatus
                });

                retryCount++;
                var attemptLatency = (int)stopwatch.ElapsedMilliseconds;

                await _metricBuffer.RecordErrorAsync(
                    userId, model.Code, c.Partner.Code, c.Key.Code,
                    attemptLatency, attemptResult.ErrorType ?? "unknown");

                await _errorRecorder.RecordAsync(
                    requestId, userId, model.Code, c.Partner.Code, c.Key.Code, c.Key.Id,
                    attemptResult.ErrorType ?? "unknown", null, attemptResult.HttpStatus,
                    attemptResult.ErrorMessage, attemptLatency, ct);

                // Cool down on noisy errors
                var cooldown = ResolveCooldown(attemptResult.ErrorType);
                if (cooldown is { } ttl)
                {
                    await _cooldownStore.SetAccountKeyModelCooldownAsync(
                        c.Key.Id, model.Code, attemptResult.ErrorType ?? "unknown",
                        attemptResult.ErrorMessage, ttl);
                }
            }
            finally
            {
                await _rateStore.DecreaseInflightAsync(c.Partner.Code, c.Key.Id);
            }
        }

        return new GenerateAiResponse {
            Success = false,
            RequestId = requestId,
            Model = model.Code,
            LatencyMs = (int)stopwatch.ElapsedMilliseconds,
            ErrorType = attemptErrors.LastOrDefault()?.ErrorType ?? "unknown",
            ErrorMessage = attemptErrors.LastOrDefault()?.ErrorMessage ?? "All providers failed.",
            Errors = attemptErrors
        };
    }

    private async Task<PartnerGenerateResult> CallPartnerAsync(
        RouteCandidate c, GenerateAiRequest req, string? systemPrompt, decimal temperature, int maxTokens, CancellationToken ct)
    {
        var client = _partnerFactory.Get(c.Partner.AdapterCode);
        var apiKey = _protector.Unprotect(c.Key.ApiKeyEnc);

        var ctx = new PartnerCallContext
        {
            ApiKey = apiKey,
            ProviderModel = c.Route.ProviderModel,
            BaseUrl = c.Partner.BaseUrl,
            TimeoutMs = c.Route.TimeoutMs
        };

        var pr = new PartnerGenerateRequest
        {
            Prompt = req.Prompt,
            SystemPrompt = systemPrompt,
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        return await client.GenerateAsync(ctx, pr, ct);
    }

    private static TimeSpan? ResolveCooldown(string? errorType) => errorType switch
    {
        "rate_limit"        => TimeSpan.FromSeconds(60),
        "quota_exceeded"    => TimeSpan.FromMinutes(15),
        "auth_error"        => TimeSpan.FromMinutes(5),
        "permission_error"  => TimeSpan.FromMinutes(5),
        "server_error"      => TimeSpan.FromSeconds(30),
        _ => null
    };

    private bool ShouldExposeRouting(GenerateAiRequest req)
        => req.Debug && _opts.ExposeRoutingWhenDebug;

    internal static string? BuildEffectiveSystemPrompt(string? systemPrompt, HttpContext? http)
    {
        if (!ShouldUseCaveman(http)) return systemPrompt;

        return string.IsNullOrWhiteSpace(systemPrompt)
            ? CavemanInstruction
            : $"{systemPrompt}\n\n{CavemanInstruction}";
    }

    private static bool ShouldUseCaveman(HttpContext? http)
    {
        var authMethod = http?.User?.FindFirst("auth_method")?.Value;
        if (!string.Equals(authMethod, "pat", StringComparison.OrdinalIgnoreCase))
            return false;

        var useCaveman = http?.User?.FindFirst("use_caveman")?.Value;
        return string.Equals(useCaveman, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static GenerateAiResponse Fail(string requestId, string model, Stopwatch sw, string type, string message)
        => new() { Success = false, RequestId = requestId, Model = model,
                   ErrorType = type, ErrorMessage = message, LatencyMs = (int)sw.ElapsedMilliseconds };
}
