using System.ComponentModel.DataAnnotations;

namespace AiGateway.Api.Options;

public sealed class AiGatewayOptions
{
    public bool RequireClientAuth { get; init; } = false;

    [Required]
    public string EncryptionKeyBase64 { get; init; } = string.Empty;

    [Range(5, 3600)]
    public int MetricFlushSeconds { get; init; } = 60;

    [Range(1, 365)]
    public int ErrorEventsRetentionDays { get; init; } = 62;

    public bool ExposeRoutingWhenDebug { get; init; } = true;

    [Range(1, 3600)]
    public int ConfigCacheSeconds { get; init; } = 60;

    [Range(0, 10000)]
    public int ErrorEventsMaxPerAccountTypeHour { get; init; } = 20;

    [Range(1, 100000)]
    public int DefaultReservedOutputTokens { get; init; } = 1000;
}
