using System.Text;
using AiGateway.Api.Contracts;

namespace AiGateway.Api.Application.AI;

public sealed class TokenEstimator
{
    /// <summary>
    /// Estimate input tokens conservatively. ASCII tokenizers run ~4 chars/token; for
    /// non-ASCII scripts (Vietnamese, CJK), bytes/3.5 is a safer floor. We pick the larger.
    /// </summary>
    public int EstimateInputTokens(string? systemPrompt, string prompt)
    {
        var sysChars = systemPrompt?.Length ?? 0;
        var sysBytes = string.IsNullOrEmpty(systemPrompt) ? 0 : Encoding.UTF8.GetByteCount(systemPrompt);
        var pChars = prompt?.Length ?? 0;
        var pBytes = string.IsNullOrEmpty(prompt) ? 0 : Encoding.UTF8.GetByteCount(prompt);

        var totalChars = sysChars + pChars;
        var totalBytes = sysBytes + pBytes;

        var byCharCount = (int)Math.Ceiling(totalChars / 2.0); // pessimistic for ASCII
        var byByteCount = (int)Math.Ceiling(totalBytes / 3.5); // ~bytes per token for UTF-8 mixed

        return Math.Max(1, Math.Max(byCharCount, byByteCount));
    }

    public int EstimateMessages(IReadOnlyList<AiMessageDto> messages)
    {
        var totalChars = 0;
        var totalBytes = 0;
        foreach (var m in messages)
        {
            var c = m.Content ?? string.Empty;
            totalChars += c.Length;
            totalBytes += Encoding.UTF8.GetByteCount(c);
        }
        var byCharCount = (int)Math.Ceiling(totalChars / 2.0);
        var byByteCount = (int)Math.Ceiling(totalBytes / 3.5);
        return Math.Max(1, Math.Max(byCharCount, byByteCount));
    }
}
