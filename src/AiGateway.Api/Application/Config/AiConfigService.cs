using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;

namespace AiGateway.Api.Application.Config;

public sealed class AiConfigService
{
    private readonly AiConfigRepository _repo;
    private readonly RedisConfigCache _cache;

    public AiConfigService(AiConfigRepository repo, RedisConfigCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public Task<AiModel?> GetModelAsync(string code, CancellationToken ct)
        => _cache.GetOrSetAsync(RedisKeys.ConfigModel(code), () => _repo.GetModelByCodeAsync(code, ct), ct);

    public Task<AiPartner?> GetPartnerAsync(string code, CancellationToken ct)
        => _cache.GetOrSetAsync(RedisKeys.ConfigPartner(code), () => _repo.GetPartnerByCodeAsync(code, ct), ct);

    public Task InvalidatePartnerAsync(string code) => _cache.DeleteAsync(RedisKeys.ConfigPartner(code));
    public Task InvalidateModelAsync(string code)   => _cache.DeleteAsync(RedisKeys.ConfigModel(code));
}
