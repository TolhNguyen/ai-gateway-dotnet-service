namespace AiGateway.Api.Contracts;

public sealed record UpsertClientRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public string? ApiKey { get; init; }
    public int? RpmLimit { get; init; }
    public int? RpdLimit { get; init; }
    public IReadOnlyList<string> AllowedModels { get; init; } = [];
    public Dictionary<string, object?> Config { get; init; } = new();
}

public sealed record UpsertModelRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public decimal DefaultTemperature { get; init; } = 0.7m;
    public int DefaultMaxTokens { get; init; } = 1000;
    public string Strategy { get; init; } = "balanced";
    public bool FallbackEnabled { get; init; } = true;
    public int MaxRetry { get; init; } = 3;
    public Dictionary<string, object?> Config { get; init; } = new();
}

public sealed record UpsertPartnerRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public required string AdapterCode { get; init; }
    public required string BaseUrl { get; init; }
    public int Weight { get; init; } = 100;
    public int Priority { get; init; } = 100;
    public int QualityScore { get; init; } = 100;
    public Dictionary<string, object?> Config { get; init; } = new();
}

public sealed record UpsertAccountRequest
{
    public required string Code { get; init; }
    public string? Name { get; init; }
    public string Status { get; init; } = "active";
    public string? AccountRef { get; init; }
    public string? ApiKey { get; init; }
    public string? ApiKeyRef { get; init; }
    public int? RpmLimit { get; init; }
    public int? RpdLimit { get; init; }
    public int? TpmLimit { get; init; }
    public int? TpdLimit { get; init; }
    public int Weight { get; init; } = 100;
    public int Priority { get; init; } = 100;
    public Dictionary<string, object?> Config { get; init; } = new();
}

public sealed record UpsertModelRouteRequest
{
    public required string PartnerCode { get; init; }
    public string RouteCode { get; init; } = "default";
    public string Status { get; init; } = "active";
    public required string ProviderModel { get; init; }
    public int TimeoutMs { get; init; } = 30000;
    public int Weight { get; init; } = 100;
    public int Priority { get; init; } = 100;
    public Dictionary<string, object?> Config { get; init; } = new();
}

public sealed record UpdateStatusRequest
{
    public required string Status { get; init; }
}
