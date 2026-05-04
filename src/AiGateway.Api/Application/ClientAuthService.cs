using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Application;

public sealed record ClientAuthResult
{
    public required string ClientCode { get; init; }
    public AiClientConfig? Client { get; init; }
}

public sealed class ClientAuthService
{
    private readonly AiConfigService _configService;
    private readonly RedisRateLimitStore _rateLimitStore;
    private readonly ApiKeyHasher _apiKeyHasher;
    private readonly AiGatewayOptions _options;

    public ClientAuthService(
        AiConfigService configService,
        RedisRateLimitStore rateLimitStore,
        ApiKeyHasher apiKeyHasher,
        IOptions<AiGatewayOptions> options)
    {
        _configService = configService;
        _rateLimitStore = rateLimitStore;
        _apiKeyHasher = apiKeyHasher;
        _options = options.Value;
    }

    public async Task<ClientAuthResult> AuthenticateAsync(
        GenerateAiRequest request,
        string? headerClientCode,
        string? headerApiKey,
        CancellationToken cancellationToken)
    {
        var clientCode = FirstNonEmpty(headerClientCode, request.ClientCode, _options.RequireClientAuth ? null : "anonymous");

        if (string.IsNullOrWhiteSpace(clientCode))
        {
            throw new UnauthorizedAccessException("Missing X-AI-Client");
        }

        var client = await _configService.GetClientByCodeAsync(clientCode, cancellationToken);

        if (client is null)
        {
            if (_options.RequireClientAuth)
            {
                throw new UnauthorizedAccessException($"AI client not found: {clientCode}");
            }

            return new ClientAuthResult { ClientCode = clientCode, Client = null };
        }

        if (!string.Equals(client.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"AI client disabled: {clientCode}");
        }

        if (_options.RequireClientAuth)
        {
            if (string.IsNullOrWhiteSpace(headerApiKey) || string.IsNullOrWhiteSpace(client.ApiKeyHash))
            {
                throw new UnauthorizedAccessException("Missing X-AI-Key");
            }

            if (!_apiKeyHasher.Verify(headerApiKey, client.ApiKeyHash))
            {
                throw new UnauthorizedAccessException("Invalid AI client key");
            }
        }

        if (client.AllowedModels.Count > 0 &&
            !client.AllowedModels.Contains("*") &&
            !client.AllowedModels.Contains(request.Model, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Client {clientCode} is not allowed to use model {request.Model}");
        }

        var passRateLimit = await _rateLimitStore.CheckAndReserveClientAsync(
            client.Code,
            client.RpmLimit,
            client.RpdLimit,
            cancellationToken);

        if (!passRateLimit)
        {
            throw new InvalidOperationException($"Client {clientCode} reached rate limit");
        }

        return new ClientAuthResult { ClientCode = client.Code, Client = client };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }
}
