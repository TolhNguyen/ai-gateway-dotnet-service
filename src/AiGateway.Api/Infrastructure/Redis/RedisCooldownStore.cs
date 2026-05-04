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

    public async Task<bool> IsAccountCooldownAsync(string accountCode)
        => await _db.KeyExistsAsync(RedisKeys.CooldownAccount(accountCode));

    public async Task<bool> IsAccountModelCooldownAsync(string accountCode, string modelCode)
        => await _db.KeyExistsAsync(RedisKeys.CooldownAccountModel(accountCode, modelCode));

    public async Task<bool> IsBlockedAsync(string partnerCode, string accountCode, string modelCode)
    {
        if (await IsPartnerCooldownAsync(partnerCode)) return true;
        if (await IsAccountCooldownAsync(accountCode)) return true;
        if (await IsAccountModelCooldownAsync(accountCode, modelCode)) return true;
        return false;
    }

    public async Task SetAccountModelCooldownAsync(
        string accountCode,
        string modelCode,
        string errorType,
        string? reason,
        TimeSpan ttl)
    {
        var value = JsonSerializer.Serialize(new CooldownInfo
        {
            Reason = reason,
            ErrorType = errorType,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var batch = _db.CreateBatch();
        _ = batch.StringSetAsync(RedisKeys.CooldownAccountModel(accountCode, modelCode), value, ttl);
        _ = batch.StringSetAsync(RedisKeys.CooldownAccount(accountCode), value, ttl);
        batch.Execute();
        await Task.CompletedTask;
    }

    public async Task<CooldownInfo?> GetAccountCooldownAsync(string accountCode)
    {
        var value = await _db.StringGetAsync(RedisKeys.CooldownAccount(accountCode));
        if (!value.HasValue) return null;

        try
        {
            return JsonSerializer.Deserialize<CooldownInfo>(value.ToString());
        }
        catch
        {
            return new CooldownInfo { Reason = value.ToString() };
        }
    }

    public async Task<CooldownInfo?> GetAccountModelCooldownAsync(string accountCode, string modelCode)
    {
        var value = await _db.StringGetAsync(RedisKeys.CooldownAccountModel(accountCode, modelCode));
        if (!value.HasValue) return null;

        try
        {
            return JsonSerializer.Deserialize<CooldownInfo>(value.ToString());
        }
        catch
        {
            return new CooldownInfo { Reason = value.ToString() };
        }
    }
}
