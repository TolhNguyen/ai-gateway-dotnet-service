using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiGateway.Api.Contracts;

namespace AiGateway.Api.Infrastructure.Partners;

public sealed class OpenAiCompatibleClient : IAiPartnerClient
{
    public string AdapterCode => "openai_compatible";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiCompatibleClient> _logger;

    public OpenAiCompatibleClient(IHttpClientFactory factory, ILogger<OpenAiCompatibleClient> logger)
    {
        _httpFactory = factory;
        _logger = logger;
    }

    public async Task<PartnerGenerateResult> GenerateAsync(
        PartnerCallContext ctx, PartnerGenerateRequest req, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(AdapterCode);
            http.Timeout = TimeSpan.FromMilliseconds(ctx.TimeoutMs);

            var url = $"{ctx.BaseUrl.TrimEnd('/')}/v1/chat/completions";

            var messages = new List<object>(2);
            if (!string.IsNullOrEmpty(req.SystemPrompt))
                messages.Add(new { role = "system", content = req.SystemPrompt });
            messages.Add(new { role = "user", content = req.Prompt });

            var bodyObj = new
            {
                model = ctx.ProviderModel,
                messages,
                temperature = (double)req.Temperature,
                max_tokens = req.MaxTokens
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, url);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.ApiKey);
            msg.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(msg, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return MapError(resp.StatusCode, raw);

            return ParseChatCompletion(raw);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PartnerGenerateResult { Success = false, ErrorType = "timeout", ErrorMessage = "OpenAI-compatible request timed out." };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI-compatible call failed");
            return new PartnerGenerateResult { Success = false, ErrorType = "unknown", ErrorMessage = ex.Message };
        }
    }

    internal static PartnerGenerateResult ParseChatCompletion(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        string? text = null;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var c))
                text = c.GetString();
        }

        int? inT = null, outT = null, totT = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var p))     inT = p.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var co)) outT = co.GetInt32();
            if (usage.TryGetProperty("total_tokens", out var t))      totT = t.GetInt32();
        }

        if (string.IsNullOrEmpty(text))
            return new PartnerGenerateResult { Success = false, ErrorType = "bad_response", ErrorMessage = "empty choices" };

        return new PartnerGenerateResult
        {
            Success = true,
            Content = text,
            Usage = new AiUsageDto { InputTokens = inT, OutputTokens = outT, TotalTokens = totT }
        };
    }

    internal static PartnerGenerateResult MapError(HttpStatusCode status, string body)
    {
        var (type, _) = status switch
        {
            HttpStatusCode.TooManyRequests   => ("rate_limit", body),
            HttpStatusCode.RequestTimeout    => ("timeout", "request timeout"),
            HttpStatusCode.Unauthorized      => ("auth_error", body),
            HttpStatusCode.Forbidden         => ("permission_error", body),
            HttpStatusCode.PaymentRequired   => ("quota_exceeded", body),
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout    => ("server_error", body),
            _ => ("unknown", body)
        };

        var lower = body.ToLowerInvariant();
        if (status == HttpStatusCode.BadRequest && (lower.Contains("quota") || lower.Contains("insufficient")))
            type = "quota_exceeded";

        return new PartnerGenerateResult
        {
            Success = false,
            ErrorType = type,
            ErrorMessage = body.Length > 500 ? body[..500] : body,
            HttpStatus = (int)status
        };
    }
}
