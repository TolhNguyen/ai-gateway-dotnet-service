namespace AiGateway.Api.Contracts;

public sealed record AiMessageDto
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

public sealed record GenerateAiRequest
{
    public required string Model { get; init; }
    public string? SystemPrompt { get; init; }
    public required string Prompt { get; init; }
    public decimal? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public string? RequestId { get; init; }
    public string? ClientCode { get; init; }
    public string? FeatureCode { get; init; }
    public bool Debug { get; init; } = false;
    public Dictionary<string, object?> Metadata { get; init; } = new();
}

public sealed record AiUsageDto
{
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
}

public sealed record AiRoutingDto
{
    public string? PartnerCode { get; init; }
    public string? AccountCode { get; init; }
    public string? RouteCode { get; init; }
    public string? ProviderModel { get; init; }
    public bool FallbackUsed { get; init; }
    public int RetryCount { get; init; }
}

public sealed record AiAttemptErrorDto
{
    public required string PartnerCode { get; init; }
    public required string AccountCode { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
    public int? HttpStatus { get; init; }
}

public sealed record GenerateAiResponse
{
    public required bool Success { get; init; }
    public required string RequestId { get; init; }
    public required string Model { get; init; }
    public string? Content { get; init; }
    public object? Data { get; init; }
    public AiUsageDto? Usage { get; init; }
    public int LatencyMs { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<AiAttemptErrorDto>? Errors { get; init; }
    public AiRoutingDto? Routing { get; init; }
}
