namespace AiGateway.Api.Infrastructure.Redis;

public static class RedisKeys
{
    public const int RuntimeRetentionSeconds = 65 * 24 * 60 * 60; // ~65 days

    // Hash tags {} ensure key co-location for Lua/MULTI on Redis Cluster.

    // ---- Account-key request/token quotas (atomic via Lua) -----------
    public static string AccountReqMinute(long keyId, string modelCode, string bucket)
        => $"ai:quota:req:{{key:{keyId}:model:{modelCode}}}:m:{bucket}";

    public static string AccountReqDay(long keyId, string modelCode, string bucket)
        => $"ai:quota:req:{{key:{keyId}:model:{modelCode}}}:d:{bucket}";

    public static string AccountTokMinute(long keyId, string modelCode, string bucket)
        => $"ai:quota:tok:{{key:{keyId}:model:{modelCode}}}:m:{bucket}";

    public static string AccountTokDay(long keyId, string modelCode, string bucket)
        => $"ai:quota:tok:{{key:{keyId}:model:{modelCode}}}:d:{bucket}";

    // ---- User-level rate limit (per-tenant fairness) ----------------
    public static string UserRateMinute(long userId, string bucket)
        => $"ai:rate:{{user:{userId}}}:m:{bucket}";

    public static string UserRateDay(long userId, string bucket)
        => $"ai:rate:{{user:{userId}}}:d:{bucket}";

    // ---- Cooldown ---------------------------------------------------
    public static string CooldownAccountKey(long keyId)
        => $"ai:cooldown:key:{keyId}";

    public static string CooldownAccountKeyModel(long keyId, string modelCode)
        => $"ai:cooldown:key:{keyId}:model:{modelCode}";

    public static string CooldownPartner(string partnerCode)
        => $"ai:cooldown:partner:{partnerCode}";

    // ---- Inflight ---------------------------------------------------
    public static string InflightAccountKey(long keyId)
        => $"ai:inflight:key:{keyId}";

    public static string InflightPartner(string partnerCode)
        => $"ai:inflight:partner:{partnerCode}";

    // ---- Metric buffer (per-user) -----------------------------------
    public static string MetricIndex()
        => "ai:metric:index";

    public static string MetricHour(
        string hourBucket,
        long userId,
        string modelCode,
        string partnerCode,
        string accountKeyCode)
        => $"ai:metric:h:{hourBucket}:user:{userId}:model:{modelCode}:partner:{partnerCode}:key:{accountKeyCode}";

    // ---- Config cache -----------------------------------------------
    public static string ConfigModel(string modelCode)         => $"ai:config:model:{modelCode}";
    public static string ConfigPartner(string partnerCode)     => $"ai:config:partner:{partnerCode}";
    public static string ConfigUserKeys(long userId)           => $"ai:config:user_keys:{userId}";
    public static string ConfigRouteCandidates(long userId, string modelCode)
        => $"ai:config:routes:{userId}:{modelCode}";

    public static string ConfigUserKeysIndex()                 => "ai:config:index:user_keys";
    public static string ConfigRoutesIndex()                   => "ai:config:index:routes";

    // ---- Error event cap (noisy errors) -----------------------------
    public static string ErrorEventCap(string hour, long keyId, string errorType)
        => $"ai:error_cap:{hour}:key:{keyId}:type:{errorType}";

    // ---- Locks (cache stampede prevention) --------------------------
    public static string ConfigLock(string scope) => $"ai:lock:cfg:{scope}";
}
