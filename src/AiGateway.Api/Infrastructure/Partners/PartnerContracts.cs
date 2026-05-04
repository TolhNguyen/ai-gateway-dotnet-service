using AiGateway.Api.Contracts;

namespace AiGateway.Api.Infrastructure.Partners;

public sealed record PartnerGenerateRequest
{
    public required IReadOnlyList<AiMessageDto> Messages { get; init; }
    public decimal? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed record PartnerCallContext
{
    public required string PartnerCode { get; init; }
    public required string AccountCode { get; init; }
    public required string ApiKey { get; init; }
    public required string BaseUrl { get; init; }
    public required string ProviderModel { get; init; }
    public int TimeoutMs { get; init; } = 30000;
}

public sealed record PartnerGenerateResult
{
    public required bool Success { get; init; }
    public string? Content { get; init; }
    public AiUsageDto? Usage { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int? HttpStatus { get; init; }
    public bool Retryable { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public string? LimitScope { get; init; }
    public int? SuggestedCooldownSeconds { get; init; }
    public object? Raw { get; init; }
}

public interface IAiPartnerClient
{
    string AdapterCode { get; }

    Task<PartnerGenerateResult> GenerateAsync(
        PartnerCallContext context,
        PartnerGenerateRequest request,
        CancellationToken cancellationToken);
}
