namespace AiGateway.Api.Infrastructure.Redis;

public static class RedisKeys
{
    public const int RuntimeRetentionSeconds = 65 * 24 * 60 * 60;

    // Dùng Lua script để đảm bảo tính nguyên tử
    public static string ClientRateMinute(string clientCode, string bucket)
    => $"ai:rate:{{client:{clientCode}}}:m:{bucket}";

    // Dùng Lua script để đảm bảo tính nguyên tử
    public static string ClientRateDay(string clientCode, string bucket)
    => $"ai:rate:{{client:{clientCode}}}:d:{bucket}";

    // Dùng Lua script để đảm bảo tính nguyên tử
    public static string AccountReqMinute(string accountCode, string modelCode, string bucket)
    => $"ai:quota:req:{{account:{accountCode}:model:{modelCode}}}:m:{bucket}";

    // Dùng Lua script để đảm bảo tính nguyên tử
    public static string AccountReqDay(string accountCode, string modelCode, string bucket)
    => $"ai:quota:req:{{account:{accountCode}:model:{modelCode}}}:d:{bucket}";

    // Dùng Lua script để đảm bảo tính nguyên tử
    public static string AccountTokMinute(string accountCode, string modelCode, string bucket)
    => $"ai:quota:tok:{{account:{accountCode}:model:{modelCode}}}:m:{bucket}";

    // Dùng Lua script để đảm bảo tính nguyên tử
    public static string AccountTokDay(string accountCode, string modelCode, string bucket)
    => $"ai:quota:tok:{{account:{accountCode}:model:{modelCode}}}:d:{bucket}";

    public static string CooldownPartner(string partnerCode)
        => $"ai:cooldown:partner:{partnerCode}";

    public static string CooldownAccount(string accountCode)
        => $"ai:cooldown:account:{accountCode}";

    public static string CooldownAccountModel(string accountCode, string modelCode)
        => $"ai:cooldown:account:{accountCode}:model:{modelCode}";

    public static string InflightPartner(string partnerCode)
        => $"ai:inflight:partner:{partnerCode}";

    public static string InflightAccount(string accountCode)
        => $"ai:inflight:account:{accountCode}";

    public static string MetricIndex()
        => "ai:metric:index";

    public static string MetricHour(
        string hourBucket,
        string clientCode,
        string modelCode,
        string partnerCode,
        string accountCode)
        => $"ai:metric:h:{hourBucket}:client:{clientCode}:model:{modelCode}:partner:{partnerCode}:account:{accountCode}";


    public static string ConfigClient(string clientCode)
        => $"ai:config:client:{clientCode}";

    public static string ConfigModel(string modelCode)
        => $"ai:config:model:{modelCode}";

    public static string ConfigRouteCandidates(string modelCode)
        => $"ai:config:route_candidates:{modelCode}";

    public static string ConfigRouteCandidatesIndex()
        => "ai:config:index:route_candidates";

    public static string ErrorEventCap(string hourBucket, string accountCode, string errorType)
        => $"ai:error_event_cap:{hourBucket}:account:{accountCode}:type:{errorType}";

    public static string ResponseCache(string hash)
        => $"ai:cache:response:{hash}";
}
