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

    private const string ClientReserveLua = """
    local rpmKey = KEYS[1]
    local rpdKey = KEYS[2]
    local rpmLimit = tonumber(ARGV[1]) or 0
    local rpdLimit = tonumber(ARGV[2]) or 0
    local minuteTtl = tonumber(ARGV[3]) or 120
    local dayTtl = tonumber(ARGV[4]) or 172800

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
      redis.call('EXPIRE', rpmKey, minuteTtl)
    end

    if rpdLimit > 0 then
      redis.call('INCRBY', rpdKey, 1)
      redis.call('EXPIRE', rpdKey, dayTtl)
    end

    return {1, 'ok', 0, 0}
    """;

    private const string AccountReserveLua = """
    local reqMinuteKey = KEYS[1]
    local reqDayKey = KEYS[2]
    local tokMinuteKey = KEYS[3]
    local tokDayKey = KEYS[4]

    local rpmLimit = tonumber(ARGV[1]) or 0
    local rpdLimit = tonumber(ARGV[2]) or 0
    local tpmLimit = tonumber(ARGV[3]) or 0
    local tpdLimit = tonumber(ARGV[4]) or 0
    local tokenInc = tonumber(ARGV[5]) or 0
    local minuteTtl = tonumber(ARGV[6]) or 120
    local dayTtl = tonumber(ARGV[7]) or 172800

    local reqMinuteUsed = tonumber(redis.call('GET', reqMinuteKey) or '0')
    local reqDayUsed = tonumber(redis.call('GET', reqDayKey) or '0')
    local tokMinuteUsed = tonumber(redis.call('GET', tokMinuteKey) or '0')
    local tokDayUsed = tonumber(redis.call('GET', tokDayKey) or '0')

    if rpmLimit > 0 and reqMinuteUsed + 1 > rpmLimit then
      return {0, 'rpm', reqMinuteUsed, rpmLimit}
    end

    if rpdLimit > 0 and reqDayUsed + 1 > rpdLimit then
      return {0, 'rpd', reqDayUsed, rpdLimit}
    end

    if tpmLimit > 0 and tokMinuteUsed + tokenInc > tpmLimit then
      return {0, 'tpm', tokMinuteUsed, tpmLimit}
    end

    if tpdLimit > 0 and tokDayUsed + tokenInc > tpdLimit then
      return {0, 'tpd', tokDayUsed, tpdLimit}
    end

    redis.call('INCRBY', reqMinuteKey, 1)
    redis.call('EXPIRE', reqMinuteKey, minuteTtl)
    redis.call('INCRBY', reqDayKey, 1)
    redis.call('EXPIRE', reqDayKey, dayTtl)

    if tokenInc > 0 then
      redis.call('INCRBY', tokMinuteKey, tokenInc)
      redis.call('EXPIRE', tokMinuteKey, minuteTtl)
      redis.call('INCRBY', tokDayKey, tokenInc)
      redis.call('EXPIRE', tokDayKey, dayTtl)
    end

    return {1, 'ok', 0, 0}
    """;

    public RedisRateLimitStore(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task<bool> CheckAndReserveClientAsync(
        string clientCode,
        int? rpmLimit,
        int? rpdLimit,
        CancellationToken cancellationToken)
    {
        if (rpmLimit is not > 0 && rpdLimit is not > 0)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var minute = now.ToString("yyyyMMddHHmm");
        var day = now.ToString("yyyyMMdd");

        var result = await _db.ScriptEvaluateAsync(
            ClientReserveLua,
            new RedisKey[]
            {
                RedisKeys.ClientRateMinute(clientCode, minute),
                RedisKeys.ClientRateDay(clientCode, day)
            },
            new RedisValue[]
            {
                rpmLimit ?? 0,
                rpdLimit ?? 0,
                120,
                172800
            });

        var parts = (RedisResult[])result!;
        return ToLong(parts[0]) == 1;
    }

    public async Task<AccountQuotaReserveResult> TryReserveAccountUsageAsync(
        AiAccountConfig account,
        string modelCode,
        int reservedTokens,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var minute = now.ToString("yyyyMMddHHmm");
        var day = now.ToString("yyyyMMdd");

        var tokenMinuteKey = RedisKeys.AccountTokMinute(account.Code, modelCode, minute);
        var tokenDayKey = RedisKeys.AccountTokDay(account.Code, modelCode, day);

        var result = await _db.ScriptEvaluateAsync(
            AccountReserveLua,
            new RedisKey[]
            {
                RedisKeys.AccountReqMinute(account.Code, modelCode, minute),
                RedisKeys.AccountReqDay(account.Code, modelCode, day),
                tokenMinuteKey,
                tokenDayKey
            },
            new RedisValue[]
            {
                account.RpmLimit ?? 0,
                account.RpdLimit ?? 0,
                account.TpmLimit ?? 0,
                account.TpdLimit ?? 0,
                Math.Max(reservedTokens, 0),
                120,
                172800
            });

        var parts = (RedisResult[])result!;
        return new AccountQuotaReserveResult
        {
            Allowed = ToLong(parts[0]) == 1,
            Reason = parts[1].ToString(),
            Used = ToLong(parts[2]),
            Limit = ToLong(parts[3]),
            TokenMinuteKey = tokenMinuteKey,
            TokenDayKey = tokenDayKey,
            ReservedTokens = Math.Max(reservedTokens, 0)
        };
    }

    public async Task AdjustReservedTokenUsageAsync(
        AccountQuotaReserveResult reservation,
        int? actualTotalTokens,
        CancellationToken cancellationToken)
    {
        if (!reservation.Allowed || reservation.ReservedTokens <= 0 || actualTotalTokens is not > 0)
        {
            return;
        }

        var delta = actualTotalTokens.Value - reservation.ReservedTokens;
        if (delta == 0)
        {
            return;
        }

        var batch = _db.CreateBatch();
        _ = batch.StringIncrementAsync(reservation.TokenMinuteKey, delta);
        _ = batch.StringIncrementAsync(reservation.TokenDayKey, delta);
        batch.Execute();
        await Task.CompletedTask;
    }

    public async Task<long> GetInflightAccountAsync(string accountCode)
    {
        var value = await _db.StringGetAsync(RedisKeys.InflightAccount(accountCode));
        return value.HasValue && long.TryParse(value.ToString(), out var result) ? result : 0;
    }

    public async Task IncreaseInflightAsync(string partnerCode, string accountCode)
    {
        var batch = _db.CreateBatch();
        _ = batch.StringIncrementAsync(RedisKeys.InflightPartner(partnerCode));
        _ = batch.KeyExpireAsync(RedisKeys.InflightPartner(partnerCode), TimeSpan.FromMinutes(10));
        _ = batch.StringIncrementAsync(RedisKeys.InflightAccount(accountCode));
        _ = batch.KeyExpireAsync(RedisKeys.InflightAccount(accountCode), TimeSpan.FromMinutes(10));
        batch.Execute();
        await Task.CompletedTask;
    }

    public async Task DecreaseInflightAsync(string partnerCode, string accountCode)
    {
        var batch = _db.CreateBatch();
        _ = batch.StringDecrementAsync(RedisKeys.InflightPartner(partnerCode));
        _ = batch.StringDecrementAsync(RedisKeys.InflightAccount(accountCode));
        batch.Execute();
        await Task.CompletedTask;
    }

    private static long ToLong(RedisResult result)
    {
        return long.TryParse(result.ToString(), out var value) ? value : 0;
    }
}
