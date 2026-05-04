using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;

namespace AiGateway.Api.Application;

public sealed class AiConfigService
{
    private readonly AiConfigRepository _repository;
    private readonly RedisConfigCache _cache;

    public AiConfigService(AiConfigRepository repository, RedisConfigCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<AiClientConfig?> GetClientByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var key = RedisKeys.ConfigClient(code);
        var cached = await _cache.GetAsync<AiClientConfig>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var client = await _repository.GetClientByCodeAsync(code);
        if (client is not null)
        {
            await _cache.SetAsync(key, client, cancellationToken);
        }

        return client;
    }

    public async Task<AiModelConfig?> GetModelByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var key = RedisKeys.ConfigModel(code);
        var cached = await _cache.GetAsync<AiModelConfig>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var model = await _repository.GetModelByCodeAsync(code);
        if (model is not null)
        {
            await _cache.SetAsync(key, model, cancellationToken);
        }

        return model;
    }

    public async Task<IReadOnlyList<AiRouteCandidate>> GetRouteCandidatesAsync(AiModelConfig model, CancellationToken cancellationToken)
    {
        var key = RedisKeys.ConfigRouteCandidates(model.Code);
        var cached = await _cache.GetAsync<List<AiRouteCandidate>>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var candidates = await _repository.GetRouteCandidatesAsync(model);
        await _cache.SetRouteCandidatesAsync(key, candidates.ToList(), cancellationToken);
        return candidates;
    }

    public async Task InvalidateClientAsync(string clientCode, CancellationToken cancellationToken = default)
    {
        await _cache.DeleteAsync(RedisKeys.ConfigClient(clientCode), cancellationToken);
    }

    public async Task InvalidateModelAsync(string modelCode, CancellationToken cancellationToken = default)
    {
        await _cache.DeleteAsync(RedisKeys.ConfigModel(modelCode), cancellationToken);
        await _cache.DeleteAsync(RedisKeys.ConfigRouteCandidates(modelCode), cancellationToken);
    }

    public async Task InvalidateRoutesAsync(CancellationToken cancellationToken = default)
    {
        await _cache.InvalidateRouteCandidatesAsync(cancellationToken);
    }
}
