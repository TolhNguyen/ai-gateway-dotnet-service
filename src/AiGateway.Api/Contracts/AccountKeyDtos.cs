using System.ComponentModel.DataAnnotations;

namespace AiGateway.Api.Contracts;

public sealed record AccountKeyDto
{
    public long Id { get; init; }
    public string PartnerCode { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Status { get; init; } = "active";
    public string ApiKeyMask { get; init; } = string.Empty;
    public string? DefaultModelCode { get; init; }
    public int? RpmLimit { get; init; }
    public int? RpdLimit { get; init; }
    public int? TpmLimit { get; init; }
    public int? TpdLimit { get; init; }
    public int Weight { get; init; }
    public int Priority { get; init; }
    public DateTimeOffset? LastHealthCheckAt { get; init; }
    public string? LastHealthStatus { get; init; }
    public string? LastHealthError { get; init; }
    public int? LastHealthLatencyMs { get; init; }
}

public sealed record CreateAccountKeyRequest
{
    [Required, StringLength(100, MinimumLength = 2),
     RegularExpression("^[a-zA-Z0-9_-]+$", ErrorMessage = "Code must be alphanumeric / underscore / hyphen")]
    public required string Code { get; init; }

    [Required, StringLength(100)]
    public required string PartnerCode { get; init; }

    [Required, StringLength(2048, MinimumLength = 8)]
    public required string ApiKey { get; init; }

    /// <summary>Model code to use by default when calling this key's partner. Optional.</summary>
    [StringLength(100)]
    public string? DefaultModelCode { get; init; }

    [StringLength(255)]
    public string? Name { get; init; }

    [Range(1, 10_000)]   public int? RpmLimit { get; init; }
    [Range(1, 1_000_000)] public int? RpdLimit { get; init; }
    [Range(1, 10_000_000)] public int? TpmLimit { get; init; }
    [Range(1, 1_000_000_000)] public int? TpdLimit { get; init; }

    [Range(1, 1000)] public int Weight { get; init; } = 100;
    [Range(1, 1000)] public int Priority { get; init; } = 100;
}

public sealed record UpdateAccountKeyRequest
{
    /// <summary>If supplied, rotates the stored API key. Leave null to keep current.</summary>
    [StringLength(2048, MinimumLength = 8)]
    public string? ApiKey { get; init; }

    [StringLength(255)]
    public string? Name { get; init; }

    [StringLength(20)]
    public string? Status { get; init; }

    /// <summary>
    /// Set to a model code to change the default model for this key.
    /// Pass an empty string "" to clear the default model.
    /// Pass null to leave it unchanged.
    /// </summary>
    [StringLength(100)]
    public string? DefaultModelCode { get; init; }

    /// <summary>Set to true when DefaultModelCode should be applied (even if null = clear).</summary>
    public bool UpdateDefaultModel { get; init; }

    [Range(1, 10_000)]   public int? RpmLimit { get; init; }
    [Range(1, 1_000_000)] public int? RpdLimit { get; init; }
    [Range(1, 10_000_000)] public int? TpmLimit { get; init; }
    [Range(1, 1_000_000_000)] public int? TpdLimit { get; init; }

    [Range(1, 1000)] public int? Weight { get; init; }
    [Range(1, 1000)] public int? Priority { get; init; }
}

public sealed record AccountKeyHealthDto
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string PartnerCode { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public string? LastHealthStatus { get; init; }
    public string? LastHealthError { get; init; }
    public int? LastHealthLatencyMs { get; init; }
    public DateTimeOffset? LastHealthCheckAt { get; init; }
    public long Inflight { get; init; }
    public bool Cooldown { get; init; }
}
