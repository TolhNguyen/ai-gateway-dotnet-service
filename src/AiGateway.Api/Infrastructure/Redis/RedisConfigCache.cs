using System.Collections.Concurrent;
using System.Text.Json;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiGateway.Api.Infrastructure.Redis;

public sealed class RedisConfigCache
{
    private readonly IDatabase _db;
    private readonly TimeSpan _ttl;

    // Single-flight per logical cache key — prevents cache stampede.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public RedisConfigCache(IConnectionMultiplexer redis, IOptions<AiGatewayOptions> opt)
    {
        _db = redis.GetDatabase();
        _ttl = TimeSpan.FromSeconds(opt.Value.ConfigCacheSeconds);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var v = await _db.StringGetAsync(key);
        if (!v.HasValue) return default;
        try { return JsonSerializer.Deserialize<T>(v.ToString()!, JsonOpts); }
        catch { return default; }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        await _db.StringSetAsync(key, JsonSerializer.Serialize(value, JsonOpts), _ttl);
    }

    public async Task SetTrackedAsync<T>(string indexKey, string key, T value, CancellationToken ct = default)
    {
        await SetAsync(key, value, ct);
        await _db.SetAddAsync(indexKey, key);
        await _db.KeyExpireAsync(indexKey, TimeSpan.FromDays(7));
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
        => await _db.KeyDeleteAsync(key);

    public async Task InvalidateIndexAsync(string indexKey, CancellationToken ct = default)
    {
        var keys = await _db.SetMembersAsync(indexKey);
        if (keys.Length > 0)
            await _db.KeyDeleteAsync(keys.Select(k => (RedisKey)k.ToString()).ToArray());
        await _db.KeyDeleteAsync(indexKey);
    }

    /// <summary>
    /// Cache-aside with single-flight: at most one DB load per cache key per process.
    /// </summary>
    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> loader, CancellationToken ct = default)
        where T : class
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            cached = await GetAsync<T>(key, ct);
            if (cached is not null) return cached;

            var loaded = await loader();
            if (loaded is not null) await SetAsync(key, loaded, ct);
            return loaded;
        }
        finally
        {
            sem.Release();
            // Keep the semaphore for reuse; the dictionary is bounded by config-key cardinality.
        }
    }
}
