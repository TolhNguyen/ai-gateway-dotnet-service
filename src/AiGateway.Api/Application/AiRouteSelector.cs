using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Redis;

namespace AiGateway.Api.Application;

public sealed class AiRouteSelector
{
    private readonly AiConfigService _configService;
    private readonly RedisRateLimitStore _rateLimitStore;
    private readonly RedisCooldownStore _cooldownStore;

    public AiRouteSelector(
        AiConfigService configService,
        RedisRateLimitStore rateLimitStore,
        RedisCooldownStore cooldownStore)
    {
        _configService = configService;
        _rateLimitStore = rateLimitStore;
        _cooldownStore = cooldownStore;
    }

    public async Task<AiRouteCandidate?> SelectAsync(
        AiModelConfig model,
        HashSet<string> attemptedAccounts,
        CancellationToken cancellationToken)
    {
        var candidates = await _configService.GetRouteCandidatesAsync(model, cancellationToken);
        var available = new List<AiRouteCandidate>();

        foreach (var candidate in candidates)
        {
            if (attemptedAccounts.Contains(candidate.Account.Code))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.Account.ApiKeyEnc))
            {
                continue;
            }

            if (await _cooldownStore.IsBlockedAsync(
                    candidate.Partner.Code,
                    candidate.Account.Code,
                    candidate.Model.Code))
            {
                continue;
            }

            var inflight = await _rateLimitStore.GetInflightAccountAsync(candidate.Account.Code);
            available.Add(candidate with { CurrentInflight = inflight });
        }

        if (available.Count == 0)
        {
            return null;
        }

        return PickWeightedRandom(available);
    }

    private static AiRouteCandidate PickWeightedRandom(IReadOnlyList<AiRouteCandidate> candidates)
    {
        var total = candidates.Sum(x => x.Score);
        var random = Random.Shared.Next(1, total + 1);
        var current = 0;

        foreach (var candidate in candidates)
        {
            current += candidate.Score;
            if (random <= current)
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}
