using System.Text.Json;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiGateway.Api.Infrastructure.Redis;

public sealed class RedisConfigCache
{
    private readonly IDatabase _db;
    private readonly TimeSpan _ttl;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RedisConfigCache(IConnectionMultiplexer redis, IOptions<AiGatewayOptions> options)
    {
        _db = redis.GetDatabase();
        _ttl = TimeSpan.FromSeconds(options.Value.ConfigCacheSeconds);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await _db.StringGetAsync(key);
        if (!value.HasValue)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await _db.StringSetAsync(key, json, _ttl);
    }

    public async Task SetRouteCandidatesAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        await SetAsync(key, value, cancellationToken);
        await _db.SetAddAsync(RedisKeys.ConfigRouteCandidatesIndex(), key);
        await _db.KeyExpireAsync(RedisKeys.ConfigRouteCandidatesIndex(), TimeSpan.FromDays(7));
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(key);
    }

    public async Task InvalidateRouteCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var indexKey = RedisKeys.ConfigRouteCandidatesIndex();
        var keys = await _db.SetMembersAsync(indexKey);
        if (keys.Length > 0)
        {
            await _db.KeyDeleteAsync(keys.Select(x => (RedisKey)x.ToString()).ToArray());
        }

        await _db.KeyDeleteAsync(indexKey);
    }
}
