using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiGateway.Api.Contracts;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Infrastructure.Partners;

/// <summary>
/// Partner client for Anthropic Claude API (api.anthropic.com/v1/messages).
/// </summary>
public sealed class ClaudeClient : IAiPartnerClient
{
    public string AdapterCode => "claude";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeClient> _logger;

    public ClaudeClient(
        IHttpClientFactory factory,
        IOptions<ClaudeOptions> options,
        ILogger<ClaudeClient> logger)
    {
        _httpFactory = factory;
        _options     = options.Value;
        _logger      = logger;
    }

    public async Task<PartnerGenerateResult> GenerateAsync(
        PartnerCallContext ctx, PartnerGenerateRequest req, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(AdapterCode);
            http.Timeout = TimeSpan.FromMilliseconds(ctx.TimeoutMs);

            var url = $"{ctx.BaseUrl.TrimEnd('/')}/v1/messages";

            // Build messages array
            var messages = new List<object>(1)
            {
                new { role = "user", content = req.Prompt }
            };

            var bodyObj = new
            {
                model       = ctx.ProviderModel,
                messages,
                system      = string.IsNullOrEmpty(req.SystemPrompt) ? null : req.SystemPrompt,
                temperature = (double)req.Temperature,
                max_tokens  = req.MaxTokens
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, url);
            msg.Headers.Add("x-api-key", ctx.ApiKey);
            msg.Headers.Add("anthropic-version", _options.AnthropicVersion);
            msg.Content = new StringContent(
                JsonSerializer.Serialize(bodyObj, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }),
                Encoding.UTF8,
                "application/json");

            using var resp = await http.SendAsync(msg, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return MapError(resp.StatusCode, raw);

            return ParseMessagesResponse(raw);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PartnerGenerateResult
            {
                Success = false, ErrorType = "timeout",
                ErrorMessage = "Claude request timed out."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Claude call failed");
            return new PartnerGenerateResult
            {
                Success = false, ErrorType = "unknown",
                ErrorMessage = ex.Message
            };
        }
    }

    // ─── Response parsing ────────────────────────────────────────────────────
    // Anthropic response shape:
    // {
    //   "id": "msg_...",
    //   "type": "message",
    //   "role": "assistant",
    //   "content": [ { "type": "text", "text": "..." } ],
    //   "usage": { "input_tokens": N, "output_tokens": M }
    // }

    internal static PartnerGenerateResult ParseMessagesResponse(string raw)
    {
        using var doc  = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        string? text = null;
        if (root.TryGetProperty("content", out var contentArr))
        {
            foreach (var block in contentArr.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "text" &&
                    block.TryGetProperty("text", out var textEl))
                {
                    text = textEl.GetString();
                    break;
                }
            }
        }

        int? inT = null, outT = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens",  out var inp)) inT = inp.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var out_)) outT = out_.GetInt32();
        }

        if (string.IsNullOrEmpty(text))
            return new PartnerGenerateResult
            {
                Success = false, ErrorType = "bad_response",
                ErrorMessage = "Empty content from Claude."
            };

        return new PartnerGenerateResult
        {
            Success = true,
            Content = text,
            Usage   = new AiUsageDto
            {
                InputTokens  = inT,
                OutputTokens = outT,
                TotalTokens  = inT.HasValue && outT.HasValue ? inT + outT : null
            }
        };
    }

    // ─── Error mapping ───────────────────────────────────────────────────────
    // Anthropic error body: { "type": "error", "error": { "type": "...", "message": "..." } }

    internal static PartnerGenerateResult MapError(HttpStatusCode status, string body)
    {
        var type = status switch
        {
            HttpStatusCode.TooManyRequests   => "rate_limit",
            HttpStatusCode.RequestTimeout    => "timeout",
            HttpStatusCode.Unauthorized      => "auth_error",
            HttpStatusCode.Forbidden         => "permission_error",
            HttpStatusCode.PaymentRequired   => "quota_exceeded",
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout    => "server_error",
            _                                => "unknown"
        };

        // Anthropic wraps errors in JSON — try to extract a cleaner message
        var message = TryExtractAnthropicError(body) ?? Truncate(body);

        // Detect overload/quota from body text when status is not explicit
        if (type == "unknown")
        {
            var lower = body.ToLowerInvariant();
            if (lower.Contains("rate_limit")  || lower.Contains("rate limit"))  type = "rate_limit";
            if (lower.Contains("overloaded"))                                    type = "server_error";
            if (lower.Contains("quota")       || lower.Contains("credit"))      type = "quota_exceeded";
        }

        return new PartnerGenerateResult
        {
            Success      = false,
            ErrorType    = type,
            ErrorMessage = message,
            HttpStatus   = (int)status
        };
    }

    private static string? TryExtractAnthropicError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var m))
                return Truncate(m.GetString() ?? body);
        }
        catch { /* not JSON — fall through */ }
        return null;
    }

    private static string Truncate(string s) => s.Length > 500 ? s[..500] : s;
}
