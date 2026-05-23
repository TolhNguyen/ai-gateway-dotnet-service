using System.ComponentModel.DataAnnotations;

namespace AiGateway.Api.Options;

public sealed class AiGatewayOptions
{
    /// <summary>AES-256 key, base64-encoded (32 bytes). Auto-generated on first run if missing.</summary>
    public string EncryptionKeyBase64 { get; set; } = string.Empty;

    /// <summary>If user table is empty, create this user as admin.</summary>
    public string BootstrapAdminEmail    { get; set; } = "[email protected]";
    public string BootstrapAdminPassword { get; set; } = "ChangeMe!2026";

    [Range(5, 3600)]    public int MetricFlushSeconds            { get; set; } = 30;
    [Range(1, 365)]     public int ErrorEventsRetentionDays      { get; set; } = 62;
    [Range(1, 3600)]    public int ConfigCacheSeconds            { get; set; } = 60;
    [Range(0, 10000)]   public int ErrorEventsMaxPerKeyTypeHour  { get; set; } = 20;
    [Range(1, 100000)]  public int DefaultReservedOutputTokens   { get; set; } = 1000;
    public bool ExposeRoutingWhenDebug { get; set; } = true;

    [Range(1024, 50_000_000)] public int MaxRequestBodyBytes { get; set; } = 1_048_576;

    [Range(1, 1440)]   public int HealthCheckIntervalMinutes { get; set; } = 5;
    [Range(1000, 60_000)] public int HealthCheckTimeoutMs   { get; set; } = 10_000;
}

public sealed class JwtOptions
{
    [Required] public string Issuer   { get; set; } = "ai-gateway";
    [Required] public string Audience { get; set; } = "ai-gateway";

    /// <summary>Signing key, base64-encoded (>= 32 bytes). Auto-generated on first run if missing.</summary>
    public string SecretBase64 { get; set; } = string.Empty;

    [Range(5, 1440)] public int AccessTokenMinutes { get; set; } = 60;
}

public sealed class OpenRouterOptions
{
    public string AppReferer { get; set; } = "https://github.com/your-org/ai-gateway";
    public string AppTitle   { get; set; } = "AI Gateway";
}

public sealed class ClaudeOptions
{
    /// <summary>
    /// Anthropic API version header value sent with every request.
    /// See https://docs.anthropic.com/en/api/versioning
    /// </summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";
}
