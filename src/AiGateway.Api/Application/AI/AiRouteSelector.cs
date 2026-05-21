using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;

namespace AiGateway.Api.Application.AI;

public sealed class AiRouteSelector
{
    private readonly AiConfigRepository _config;
    private readonly RedisRateLimitStore _rateStore;
    private readonly RedisCooldownStore _cooldownStore;

    public AiRouteSelector(
        AiConfigRepository config,
        RedisRateLimitStore rateStore,
        RedisCooldownStore cooldownStore)
    {
        _config = config;
        _rateStore = rateStore;
        _cooldownStore = cooldownStore;
    }

    public async Task<IReadOnlyList<RouteCandidate>> GetCandidatesAsync(
        long userId, AiModel model, CancellationToken ct)
    {
        var raw = await _config.GetUserRouteCandidatesAsync(userId, model.Code, ct);
        if (raw.Count == 0) return Array.Empty<RouteCandidate>();

        var candidates = new List<RouteCandidate>(raw.Count);
        foreach (var (route, partner, key) in raw)
        {
            var blocked = await _cooldownStore.IsBlockedAsync(partner.Code, key.Id, model.Code);
            if (blocked) continue;

            var inflight = await _rateStore.GetInflightForKeyAsync(key.Id);
            candidates.Add(new RouteCandidate
            {
                Model = model,
                Partner = partner,
                Route = route,
                Key = key,
                CurrentInflight = inflight
            });
        }

        // Sort by score (higher first), then priority (lower first).
        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Route.Priority)
            .ThenBy(c => c.Partner.Priority)
            .ToList();
    }
}
