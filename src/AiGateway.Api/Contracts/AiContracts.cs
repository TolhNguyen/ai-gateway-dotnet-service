using System.ComponentModel.DataAnnotations;

namespace AiGateway.Api.Contracts;

public sealed record AiMessageDto
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

public sealed record GenerateAiRequest
{
    [StringLength(100)]
    public string? Model { get; init; }

    [StringLength(50_000)]
    public string? SystemPrompt { get; init; }

    [Required, StringLength(200_000, MinimumLength = 1)]
    public required string Prompt { get; init; }

    [Range(typeof(decimal), "0.0", "2.0")]
    public decimal? Temperature { get; init; }

    [Range(1, 32_000)]
    public int? MaxTokens { get; init; }

    [StringLength(100)]
    public string? RequestId { get; init; }

    [StringLength(100)]
    public string? FeatureCode { get; init; }

    public bool Debug { get; init; }

    public Dictionary<string, object?> Metadata { get; init; } = new();
}

public sealed record AvailableModelDto
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string PartnerCode { get; init; }
    public required string PartnerName { get; init; }
    public decimal DefaultTemperature { get; init; }
    public int DefaultMaxTokens { get; init; }
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
    public string? AccountKeyCode { get; init; }
    public string? RouteCode { get; init; }
    public string? ProviderModel { get; init; }
    public bool FallbackUsed { get; init; }
    public int RetryCount { get; init; }
}

public sealed record AiAttemptErrorDto
{
    public required string PartnerCode { get; init; }
    public required string AccountKeyCode { get; init; }
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
    public AiUsageDto? Usage { get; init; }
    public int LatencyMs { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<AiAttemptErrorDto>? Errors { get; init; }
    public AiRoutingDto? Routing { get; init; }
}
