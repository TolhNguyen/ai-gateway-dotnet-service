using System.ComponentModel.DataAnnotations;

namespace AiGateway.Api.Contracts;

public sealed record UpsertPartnerRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public required string Code { get; init; }

    [Required, StringLength(255)]
    public required string Name { get; init; }

    [Required, StringLength(100)]
    public required string AdapterCode { get; init; }   // gemini | openai_compatible | openrouter

    [Required, StringLength(500), Url]
    public required string BaseUrl { get; init; }

    [StringLength(255)]
    public string? HealthCheckModel { get; init; }

    [StringLength(20)]
    public string Status { get; init; } = "active";

    [Range(1, 1000)] public int Weight       { get; init; } = 100;
    [Range(1, 1000)] public int Priority     { get; init; } = 100;
    [Range(1, 1000)] public int QualityScore { get; init; } = 100;
}

public sealed record UpsertModelRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public required string Code { get; init; }

    [Required, StringLength(255)]
    public required string Name { get; init; }

    [StringLength(20)]
    public string Status { get; init; } = "active";

    [Range(typeof(decimal), "0.0", "2.0")]
    public decimal DefaultTemperature { get; init; } = 0.7m;

    [Range(1, 32000)]
    public int DefaultMaxTokens { get; init; } = 1000;

    [StringLength(100)]
    public string Strategy { get; init; } = "balanced";

    public bool FallbackEnabled { get; init; } = true;

    [Range(0, 10)]
    public int MaxRetry { get; init; } = 3;
}

public sealed record UpsertRouteRequest
{
    [Required, StringLength(100)]
    public required string PartnerCode { get; init; }

    [StringLength(100)]
    public string RouteCode { get; init; } = "default";

    [Required, StringLength(255)]
    public required string ProviderModel { get; init; }

    [StringLength(20)]
    public string Status { get; init; } = "active";

    [Range(1000, 600000)]
    public int TimeoutMs { get; init; } = 30000;

    [Range(1, 1000)] public int Weight   { get; init; } = 100;
    [Range(1, 1000)] public int Priority { get; init; } = 100;
}

public sealed record UpdateStatusRequest
{
    [Required, StringLength(20)]
    public required string Status { get; init; }
}
