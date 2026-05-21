using System.Text.Json;
using StackExchange.Redis;

namespace AiGateway.Api.Infrastructure.Redis;

public sealed record CooldownInfo
{
    public string? Reason { get; init; }
    public string? ErrorType { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class RedisCooldownStore
{
    private readonly IDatabase _db;

    public RedisCooldownStore(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task<bool> IsPartnerCooldownAsync(string partnerCode)
        => await _db.KeyExistsAsync(RedisKeys.CooldownPartner(partnerCode));

    public async Task<bool> IsAccountKeyCooldownAsync(long keyId)
        => await _db.KeyExistsAsync(RedisKeys.CooldownAccountKey(keyId));

    public async Task<bool> IsAccountKeyModelCooldownAsync(long keyId, string modelCode)
        => await _db.KeyExistsAsync(RedisKeys.CooldownAccountKeyModel(keyId, modelCode));

    public async Task<bool> IsBlockedAsync(string partnerCode, long keyId, string modelCode)
        => await IsPartnerCooldownAsync(partnerCode)
        || await IsAccountKeyCooldownAsync(keyId)
        || await IsAccountKeyModelCooldownAsync(keyId, modelCode);

    public async Task SetAccountKeyModelCooldownAsync(
        long keyId, string modelCode, string errorType, string? reason, TimeSpan ttl)
    {
        var json = JsonSerializer.Serialize(new CooldownInfo
        {
            Reason = reason, ErrorType = errorType, CreatedAt = DateTimeOffset.UtcNow
        });

        var batch = _db.CreateBatch();
        _ = batch.StringSetAsync(RedisKeys.CooldownAccountKeyModel(keyId, modelCode), json, ttl);
        _ = batch.StringSetAsync(RedisKeys.CooldownAccountKey(keyId), json, ttl);
        batch.Execute();
        await Task.CompletedTask;
    }

    public async Task<CooldownInfo?> GetAccountKeyCooldownAsync(long keyId)
    {
        var v = await _db.StringGetAsync(RedisKeys.CooldownAccountKey(keyId));
        if (!v.HasValue) return null;
        try { return JsonSerializer.Deserialize<CooldownInfo>(v.ToString()!); }
        catch { return new CooldownInfo { Reason = v.ToString() }; }
    }
}
