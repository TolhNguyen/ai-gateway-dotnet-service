using AiGateway.Api.Contracts;

namespace AiGateway.Api.Infrastructure.Partners;

public sealed record PartnerCallContext
{
    public required string ApiKey { get; init; }
    public required string ProviderModel { get; init; }
    public required string BaseUrl { get; init; }
    public required int TimeoutMs { get; init; }
}

public sealed record PartnerGenerateRequest
{
    public required string Prompt { get; init; }
    public string? SystemPrompt { get; init; }
    public decimal Temperature { get; init; } = 0.7m;
    public int MaxTokens { get; init; } = 1000;
}

public sealed record PartnerGenerateResult
{
    public required bool Success { get; init; }
    public string? Content { get; init; }
    public AiUsageDto? Usage { get; init; }
    public string? ErrorType { get; init; }          // rate_limit | quota_exceeded | timeout | server_error | auth_error | permission_error | bad_response | unknown
    public string? ErrorMessage { get; init; }
    public int? HttpStatus { get; init; }
}

public interface IAiPartnerClient
{
    string AdapterCode { get; }
    Task<PartnerGenerateResult> GenerateAsync(
        PartnerCallContext ctx, PartnerGenerateRequest req, CancellationToken ct);
}
