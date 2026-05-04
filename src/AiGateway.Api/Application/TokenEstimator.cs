using AiGateway.Api.Contracts;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Application;

public sealed class TokenEstimator
{
    private readonly AiGatewayOptions _options;

    public TokenEstimator(IOptions<AiGatewayOptions> options)
    {
        _options = options.Value;
    }

    public int EstimateInputTokens(IReadOnlyList<AiMessageDto> messages)
    {
        var charCount = messages.Sum(x => x.Content?.Length ?? 0);

        // Conservative heuristic for Vietnamese and mixed-language text.
        // Providers bill with their own tokenizer; this is only a pre-call reservation.
        return Math.Max(1, (int)Math.Ceiling(charCount / 2.0));
    }

    public int EstimateReservedTokens(IReadOnlyList<AiMessageDto> messages, int? maxOutputTokens)
    {
        var inputTokens = EstimateInputTokens(messages);
        var outputTokens = Math.Max(maxOutputTokens ?? _options.DefaultReservedOutputTokens, 1);
        return inputTokens + outputTokens;
    }
}
