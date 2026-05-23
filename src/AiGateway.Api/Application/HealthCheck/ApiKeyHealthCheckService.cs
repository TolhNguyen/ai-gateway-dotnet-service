using System.Diagnostics;
using AiGateway.Api.Application.Config;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Partners;
using AiGateway.Api.Infrastructure.Security;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Application.HealthCheck;

public sealed class ApiKeyHealthCheckService
{
    private readonly AiConfigService _config;
    private readonly AccountKeyRepository _repo;
    private readonly PartnerClientFactory _partnerFactory;
    private readonly ISecretProtector _protector;
    private readonly AiGatewayOptions _opts;
    private readonly ILogger<ApiKeyHealthCheckService> _logger;

    public ApiKeyHealthCheckService(
        AiConfigService config,
        AccountKeyRepository repo,
        PartnerClientFactory partnerFactory,
        ISecretProtector protector,
        IOptions<AiGatewayOptions> opts,
        ILogger<ApiKeyHealthCheckService> logger)
    {
        _config = config;
        _repo = repo;
        _partnerFactory = partnerFactory;
        _protector = protector;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<(string status, string? error, int? latencyMs)> CheckAsync(UserAccountKey key, CancellationToken ct)
    {
        var partner = await _config.GetPartnerAsync(key.PartnerCode, ct);
        if (partner is null)
            return ("error", "Partner not found.", null);

        if (string.IsNullOrWhiteSpace(partner.HealthCheckModel))
            return ("unknown", "No health_check_model configured for partner.", null);

        if (!_partnerFactory.Has(partner.AdapterCode))
            return ("error", $"No adapter registered for '{partner.AdapterCode}'.", null);

        var client = _partnerFactory.Get(partner.AdapterCode);

        string apiKey;
        try
        {
            apiKey = _protector.Unprotect(key.ApiKeyEnc);
        }
        catch (Exception ex)
        {
            return ("error", $"Cannot decrypt API key: {ex.Message}", null);
        }

        var ctx = new PartnerCallContext
        {
            ApiKey = apiKey,
            ProviderModel = partner.HealthCheckModel,
            BaseUrl = partner.BaseUrl,
            TimeoutMs = _opts.HealthCheckTimeoutMs
        };

        var req = new PartnerGenerateRequest
        {
            Prompt = "ping",
            SystemPrompt = null,
            Temperature = 0m,
            MaxTokens = 100
        };

        var sw = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(_opts.HealthCheckTimeoutMs + 1000));

            var result = await client.GenerateAsync(ctx, req, timeoutCts.Token);
            sw.Stop();

            var latency = (int)sw.ElapsedMilliseconds;

            if (result.Success)
            {
                await _repo.UpdateHealthAsync(key.Id, "ok", null, latency, ct);
                return ("ok", null, latency);
            }

            var status = result.ErrorType is "auth_error" or "permission_error" or "quota_exceeded"
                ? "error"
                : "degraded";

            await _repo.UpdateHealthAsync(key.Id, status, $"{result.ErrorType}: {result.ErrorMessage}", latency, ct);
            return (status, result.ErrorMessage, latency);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var latency = (int)sw.ElapsedMilliseconds;
            _logger.LogWarning(ex, "Health check failed for key {Id}", key.Id);
            await _repo.UpdateHealthAsync(key.Id, "error", ex.Message, latency, ct);
            return ("error", ex.Message, latency);
        }
    }
}
