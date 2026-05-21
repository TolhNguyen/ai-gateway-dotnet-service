using AiGateway.Api.Domain;
using StackExchange.Redis;

namespace AiGateway.Api.Infrastructure.Redis;

public sealed record AccountQuotaReserveResult
{
    public required bool Allowed { get; init; }
    public string Reason { get; init; } = "ok";
    public long Used { get; init; }
    public long Limit { get; init; }
    public required string TokenMinuteKey { get; init; }
    public required string TokenDayKey { get; init; }
    public int ReservedTokens { get; init; }
}

public sealed class RedisRateLimitStore
{
    private readonly IDatabase _db;

    // ─── Lua: client/user rate-limit check-and-reserve ───────────────────────
    private const string UserReserveLua = """
    local rpmKey   = KEYS[1]
    local rpdKey   = KEYS[2]
    local rpmLimit = tonumber(ARGV[1]) or 0
    local rpdLimit = tonumber(ARGV[2]) or 0
    local minTtl   = tonumber(ARGV[3]) or 120
    local dayTtl   = tonumber(ARGV[4]) or 172800

    local rpmUsed = tonumber(redis.call('GET', rpmKey) or '0')
    local rpdUsed = tonumber(redis.call('GET', rpdKey) or '0')

    if rpmLimit > 0 and rpmUsed + 1 > rpmLimit then
      return {0, 'rpm', rpmUsed, rpmLimit}
    end
    if rpdLimit > 0 and rpdUsed + 1 > rpdLimit then
      return {0, 'rpd', rpdUsed, rpdLimit}
    end

    if rpmLimit > 0 then
      redis.call('INCRBY', rpmKey, 1)
      redis.call('EXPIRE', rpmKey, minTtl)
    end
    if rpdLimit > 0 then
      redis.call('INCRBY', rpdKey, 1)
      redis.call('EXPIRE', rpdKey, dayTtl)
    end
    return {1, 'ok', 0, 0}
    """;

    // ─── Lua: account-key request + token quota check-and-reserve ──────────
    private const string AccountReserveLua = """
    local reqMin = KEYS[1]
    local reqDay = KEYS[2]
    local tokMin = KEYS[3]
    local tokDay = KEYS[4]

    local rpmLim = tonumber(ARGV[1]) or 0
    local rpdLim = tonumber(ARGV[2]) or 0
    local tpmLim = tonumber(ARGV[3]) or 0
    local tpdLim = tonumber(ARGV[4]) or 0
    local tokInc = tonumber(ARGV[5]) or 0
    local minTtl = tonumber(ARGV[6]) or 120
    local dayTtl = tonumber(ARGV[7]) or 172800

    local rMinU = tonumber(redis.call('GET', reqMin) or '0')
    local rDayU = tonumber(redis.call('GET', reqDay) or '0')
    local tMinU = tonumber(redis.call('GET', tokMin) or '0')
    local tDayU = tonumber(redis.call('GET', tokDay) or '0')

    if rpmLim > 0 and rMinU + 1 > rpmLim then return {0, 'rpm', rMinU, rpmLim} end
    if rpdLim > 0 and rDayU + 1 > rpdLim then return {0, 'rpd', rDayU, rpdLim} end
    if tpmLim > 0 and tMinU + tokInc > tpmLim then return {0, 'tpm', tMinU, tpmLim} end
    if tpdLim > 0 and tDayU + tokInc > tpdLim then return {0, 'tpd', tDayU, tpdLim} end

    redis.call('INCRBY', reqMin, 1); redis.call('EXPIRE', reqMin, minTtl)
    redis.call('INCRBY', reqDay, 1); redis.call('EXPIRE', reqDay, dayTtl)
    if tokInc > 0 then
      redis.call('INCRBY', tokMin, tokInc); redis.call('EXPIRE', tokMin, minTtl)
      redis.call('INCRBY', tokDay, tokInc); redis.call('EXPIRE', tokDay, dayTtl)
    end
    return {1, 'ok', 0, 0}
    """;

    // ─── Lua: token-usage adjustment AFTER provider returns actual usage ──
    //
    // CRITICAL FIX: If the minute/day key has already expired (slow provider
    // > 60s), do NOT recreate it with a leftover delta — that would either
    // leak quota (positive delta) or, worse, create a key with negative value
    // and no TTL, breaking quota checks for the next bucket window.
    //
    // We also clamp at 0 to handle cases where actual usage < reserved.
    private const string AdjustTokenLua = """
    local minKey = KEYS[1]
    local dayKey = KEYS[2]
    local delta  = tonumber(ARGV[1])

    if redis.call('EXISTS', minKey) == 1 then
      local v = redis.call('INCRBY', minKey, delta)
      if tonumber(v) < 0 then
        redis.call('SET', minKey, 0, 'KEEPTTL')
      end
    end
    if redis.call('EXISTS', dayKey) == 1 then
      local v = redis.call('INCRBY', dayKey, delta)
      if tonumber(v) < 0 then
        redis.call('SET', dayKey, 0, 'KEEPTTL')
      end
    end
    return 1
    """;

    // ─── Lua: bounded INCR/DECR for inflight counters ──────────────────
    //
    // CRITICAL FIX: Plain DECR after key expiry would create the key at -1
    // with no TTL, breaking load-balancing scores forever.
    private const string IncInflightLua = """
    local key   = KEYS[1]
    local ttl   = tonumber(ARGV[1]) or 600
    local v     = redis.call('INCR', key)
    redis.call('EXPIRE', key, ttl)
    return v
    """;

    private const string DecInflightLua = """
    local key = KEYS[1]
    if redis.call('EXISTS', key) == 0 then return 0 end
    local v = redis.call('DECR', key)
    if tonumber(v) < 0 then
      redis.call('SET', key, 0, 'KEEPTTL')
      return 0
    end
    return v
    """;

    public RedisRateLimitStore(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    /// <summary>Atomic user/tenant rate-limit check-and-reserve.</summary>
    public async Task<bool> CheckAndReserveUserAsync(
        long userId, int? rpmLimit, int? rpdLimit, CancellationToken ct)
    {
        if (rpmLimit is not > 0 && rpdLimit is not > 0) return true;

        var now    = DateTimeOffset.UtcNow;
        var minute = now.ToString("yyyyMMddHHmm");
        var day    = now.ToString("yyyyMMdd");

        var result = await _db.ScriptEvaluateAsync(
            UserReserveLua,
            keys:   [ RedisKeys.UserRateMinute(userId, minute),
                      RedisKeys.UserRateDay(userId, day) ],
            values: [ rpmLimit ?? 0, rpdLimit ?? 0, 120, 172800 ]);

        var parts = (RedisResult[])result!;
        return ToLong(parts[0]) == 1;
    }

    /// <summary>Atomic per-key request + token quota check-and-reserve.</summary>
    public async Task<AccountQuotaReserveResult> TryReserveAccountUsageAsync(
        UserAccountKey key, string modelCode, int reservedTokens, CancellationToken ct)
    {
        var now    = DateTimeOffset.UtcNow;
        var minute = now.ToString("yyyyMMddHHmm");
        var day    = now.ToString("yyyyMMdd");

        var tokenMinuteKey = RedisKeys.AccountTokMinute(key.Id, modelCode, minute);
        var tokenDayKey    = RedisKeys.AccountTokDay(key.Id, modelCode, day);

        var result = await _db.ScriptEvaluateAsync(
            AccountReserveLua,
            keys:   [ RedisKeys.AccountReqMinute(key.Id, modelCode, minute),
                      RedisKeys.AccountReqDay(key.Id, modelCode, day),
                      tokenMinuteKey,
                      tokenDayKey ],
            values: [ key.RpmLimit ?? 0,
                      key.RpdLimit ?? 0,
                      key.TpmLimit ?? 0,
                      key.TpdLimit ?? 0,
                      Math.Max(reservedTokens, 0),
                      120,
                      172800 ]);

        var parts = (RedisResult[])result!;
        return new AccountQuotaReserveResult
        {
            Allowed = ToLong(parts[0]) == 1,
            Reason = parts[1].ToString()!,
            Used = ToLong(parts[2]),
            Limit = ToLong(parts[3]),
            TokenMinuteKey = tokenMinuteKey,
            TokenDayKey = tokenDayKey,
            ReservedTokens = Math.Max(reservedTokens, 0)
        };
    }

    public async Task AdjustReservedTokenUsageAsync(
        AccountQuotaReserveResult reservation, int? actualTotalTokens, CancellationToken ct)
    {
        if (!reservation.Allowed || reservation.ReservedTokens <= 0 || actualTotalTokens is not > 0)
            return;

        var delta = actualTotalTokens.Value - reservation.ReservedTokens;
        if (delta == 0) return;

        await _db.ScriptEvaluateAsync(
            AdjustTokenLua,
            keys:   [ reservation.TokenMinuteKey, reservation.TokenDayKey ],
            values: [ delta ]);
    }

    public async Task<long> GetInflightForKeyAsync(long keyId)
    {
        var v = await _db.StringGetAsync(RedisKeys.InflightAccountKey(keyId));
        return v.HasValue && long.TryParse(v.ToString(), out var n) ? n : 0;
    }

    public async Task IncreaseInflightAsync(string partnerCode, long keyId)
    {
        // 10-minute TTL: defensive cap if a request hangs forever.
        await _db.ScriptEvaluateAsync(IncInflightLua,
            keys:   [ RedisKeys.InflightPartner(partnerCode) ], values: [ 600 ]);
        await _db.ScriptEvaluateAsync(IncInflightLua,
            keys:   [ RedisKeys.InflightAccountKey(keyId)     ], values: [ 600 ]);
    }

    public async Task DecreaseInflightAsync(string partnerCode, long keyId)
    {
        await _db.ScriptEvaluateAsync(DecInflightLua,
            keys: [ RedisKeys.InflightPartner(partnerCode) ]);
        await _db.ScriptEvaluateAsync(DecInflightLua,
            keys: [ RedisKeys.InflightAccountKey(keyId)    ]);
    }

    private static long ToLong(RedisResult r)
        => long.TryParse(r.ToString(), out var v) ? v : 0;
}
