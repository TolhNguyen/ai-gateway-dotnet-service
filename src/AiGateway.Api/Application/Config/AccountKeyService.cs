using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Infrastructure.Security;

namespace AiGateway.Api.Application.Config;

public sealed class AccountKeyService
{
    private readonly AccountKeyRepository _repo;
    private readonly AiConfigRepository _config;
    private readonly ISecretProtector _protector;
    private readonly TokenHasher _hasher;
    private readonly RedisConfigCache _cache;

    public AccountKeyService(
        AccountKeyRepository repo,
        AiConfigRepository config,
        ISecretProtector protector,
        TokenHasher hasher,
        RedisConfigCache cache)
    {
        _repo = repo;
        _config = config;
        _protector = protector;
        _hasher = hasher;
        _cache = cache;
    }

    public async Task<IReadOnlyList<AccountKeyDto>> ListAsync(long userId, CancellationToken ct)
    {
        var list = await _repo.ListForUserAsync(userId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<AccountKeyDto?> GetAsync(long userId, long id, CancellationToken ct)
    {
        var k = await _repo.FindByIdAsync(userId, id, ct);
        return k is null ? null : ToDto(k);
    }

    public async Task<(bool ok, string? error, AccountKeyDto? dto)> CreateAsync(
        long userId, CreateAccountKeyRequest req, CancellationToken ct)
    {
        var partner = await _config.GetPartnerByCodeAsync(req.PartnerCode, ct);
        if (partner is null) return (false, $"Unknown partner '{req.PartnerCode}'.", null);

        if (!string.IsNullOrWhiteSpace(req.DefaultModelCode))
        {
            var routes = await _config.ListRoutesForModelAsync(req.DefaultModelCode, ct);
            if (!routes.Any(r => r.PartnerCode == req.PartnerCode && r.Status == "active"))
            {
                return (false, $"Model '{req.DefaultModelCode}' is not supported by partner '{req.PartnerCode}'.", null);
            }
        }

        var enc = _protector.Protect(req.ApiKey);
        var fp = _hasher.FingerprintHex(req.ApiKey);

        try
        {
            var created = await _repo.CreateAsync(
                userId, partner.Id, req.Code, req.Name, enc, fp,
                req.RpmLimit, req.RpdLimit, req.TpmLimit, req.TpdLimit,
                req.Weight, req.Priority, req.DefaultModelCode, ct);

            await InvalidateUserCacheAsync(userId);
            return (true, null, ToDto(created));
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate"))
        {
            return (false, $"Account key code '{req.Code}' already exists for this user.", null);
        }
    }

    public async Task<(bool ok, string? error, AccountKeyDto? dto)> UpdateAsync(
        long userId, long id, UpdateAccountKeyRequest req, CancellationToken ct)
    {
        var existing = await _repo.FindByIdAsync(userId, id, ct);
        if (existing is null) return (false, "Not found.", null);

        string? defaultModelCode = null;
        if (req.UpdateDefaultModel)
        {
            defaultModelCode = string.IsNullOrEmpty(req.DefaultModelCode) ? null : req.DefaultModelCode;
            if (defaultModelCode != null)
            {
                var partnerCode = existing.PartnerCode;
                var routes = await _config.ListRoutesForModelAsync(defaultModelCode, ct);
                if (!routes.Any(r => r.PartnerCode == partnerCode && r.Status == "active"))
                {
                    return (false, $"Model '{defaultModelCode}' is not supported by partner '{partnerCode}'.", null);
                }
            }
        }

        string? newEnc = null, newFp = null;
        if (!string.IsNullOrWhiteSpace(req.ApiKey))
        {
            newEnc = _protector.Protect(req.ApiKey);
            newFp = _hasher.FingerprintHex(req.ApiKey);
        }

        var n = await _repo.UpdateAsync(userId, id,
            newEnc, newFp, req.Name, req.Status,
            req.RpmLimit, req.RpdLimit, req.TpmLimit, req.TpdLimit,
            req.Weight, req.Priority,
            defaultModelCode, req.UpdateDefaultModel, ct);

        if (n == 0) return (false, "Not found.", null);

        await InvalidateUserCacheAsync(userId);
        var refreshed = await _repo.FindByIdAsync(userId, id, ct);
        return (true, null, refreshed is null ? null : ToDto(refreshed));
    }

    public async Task<int> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var n = await _repo.DeleteAsync(userId, id, ct);
        if (n > 0) await InvalidateUserCacheAsync(userId);
        return n;
    }

    public async Task<UserAccountKey?> GetEntityAsync(long userId, long id, CancellationToken ct)
        => await _repo.FindByIdAsync(userId, id, ct);

    public string DecryptKey(UserAccountKey key) => _protector.Unprotect(key.ApiKeyEnc);

    private async Task InvalidateUserCacheAsync(long userId)
    {
        // Drop both the key list and any cached route candidates for this user.
        await _cache.DeleteAsync(RedisKeys.ConfigUserKeys(userId));
        // Route candidates are per (user, model). We don't know which models without listing them;
        // the lazy approach is to delete prefix-based candidates via index. For MVP we'll just expire
        // them naturally — the TTL is short (default 60s).
    }

    public static AccountKeyDto ToDto(UserAccountKey k) => new()
    {
        Id = k.Id, PartnerCode = k.PartnerCode, Code = k.Code, Name = k.Name, Status = k.Status,
        ApiKeyMask = Mask(k.ApiKeyFingerprint),
        DefaultModelCode = k.DefaultModelCode,
        RpmLimit = k.RpmLimit, RpdLimit = k.RpdLimit, TpmLimit = k.TpmLimit, TpdLimit = k.TpdLimit,
        Weight = k.Weight, Priority = k.Priority,
        LastHealthCheckAt = k.LastHealthCheckAt,
        LastHealthStatus = k.LastHealthStatus,
        LastHealthError = k.LastHealthError,
        LastHealthLatencyMs = k.LastHealthLatencyMs
    };

    private static string Mask(string fingerprint)
        => fingerprint.Length >= 8 ? $"sha256:{fingerprint[..8]}..." : $"sha256:{fingerprint}";
}
