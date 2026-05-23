namespace AiGateway.Api.Domain;

public sealed record AiPartner
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public string AdapterCode { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string? HealthCheckModel { get; init; }
    public int Weight { get; init; } = 100;
    public int Priority { get; init; } = 100;
    public int QualityScore { get; init; } = 100;
}

public sealed record AiModel
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public decimal DefaultTemperature { get; init; } = 0.7m;
    public int DefaultMaxTokens { get; init; } = 1000;
    public string Strategy { get; init; } = "balanced";
    public bool FallbackEnabled { get; init; } = true;
    public int MaxRetry { get; init; } = 3;
}

public sealed record AiModelRoute
{
    public long Id { get; init; }
    public long ModelId { get; init; }
    public long PartnerId { get; init; }
    public string ModelCode { get; init; } = string.Empty;
    public string PartnerCode { get; init; } = string.Empty;
    public string RouteCode { get; init; } = "default";
    public string Status { get; init; } = "active";
    public string ProviderModel { get; init; } = string.Empty;
    public int TimeoutMs { get; init; } = 30000;
    public int Weight { get; init; } = 100;
    public int Priority { get; init; } = 100;
}

/// <summary>Per-user provider key.</summary>
public sealed record UserAccountKey
{
    public long Id { get; init; }
    public long UserId { get; init; }
    public long PartnerId { get; init; }
    public string PartnerCode { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Status { get; init; } = "active";
    public string ApiKeyEnc { get; init; } = string.Empty;
    public string ApiKeyFingerprint { get; init; } = string.Empty;
    public int? RpmLimit { get; init; }
    public int? RpdLimit { get; init; }
    public int? TpmLimit { get; init; }
    public int? TpdLimit { get; init; }
    public int Weight { get; init; } = 100;
    public int Priority { get; init; } = 100;
    public string? DefaultModelCode { get; init; }
    public DateTimeOffset? LastHealthCheckAt { get; init; }
    public string? LastHealthStatus { get; init; }
    public string? LastHealthError { get; init; }
    public int? LastHealthLatencyMs { get; init; }
}

/// <summary>Joined view for route selection.</summary>
public sealed record RouteCandidate
{
    public required AiModel Model { get; init; }
    public required AiPartner Partner { get; init; }
    public required AiModelRoute Route { get; init; }
    public required UserAccountKey Key { get; init; }
    public long CurrentInflight { get; init; }

    public int Score
    {
        get
        {
            var inflightPenalty = (int)Math.Min(CurrentInflight * 5, 500);
            var score = Partner.Weight + Partner.QualityScore + Key.Weight + Route.Weight - inflightPenalty;
            return Math.Max(score, 1);
        }
    }
}
