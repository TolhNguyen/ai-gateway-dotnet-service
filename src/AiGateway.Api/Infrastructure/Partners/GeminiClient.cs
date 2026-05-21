using System.Net;
using System.Text;
using System.Text.Json;
using AiGateway.Api.Contracts;

namespace AiGateway.Api.Infrastructure.Partners;

public sealed class GeminiClient : IAiPartnerClient
{
    public string AdapterCode => "gemini";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(IHttpClientFactory factory, ILogger<GeminiClient> logger)
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

            var url = $"{ctx.BaseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(ctx.ProviderModel)}:generateContent?key={Uri.EscapeDataString(ctx.ApiKey)}";

            var bodyObj = new
            {
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = req.Prompt } } }
                },
                systemInstruction = string.IsNullOrEmpty(req.SystemPrompt) ? null : new
                {
                    parts = new[] { new { text = req.SystemPrompt } }
                },
                generationConfig = new
                {
                    temperature = (double)req.Temperature,
                    maxOutputTokens = req.MaxTokens
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return MapError(resp.StatusCode, raw);

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string? text = null;
            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var cand = candidates[0];
                if (cand.TryGetProperty("content", out var contentEl) &&
                    contentEl.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var txt))
                {
                    text = txt.GetString();
                }
            }

            int? inT = null, outT = null, totT = null;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var p))     inT = p.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var c)) outT = c.GetInt32();
                if (usage.TryGetProperty("totalTokenCount", out var t))      totT = t.GetInt32();
            }

            if (string.IsNullOrEmpty(text))
                return new PartnerGenerateResult { Success = false, ErrorType = "bad_response", ErrorMessage = "empty candidates" };

            return new PartnerGenerateResult
            {
                Success = true,
                Content = text,
                Usage = new AiUsageDto { InputTokens = inT, OutputTokens = outT, TotalTokens = totT }
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PartnerGenerateResult { Success = false, ErrorType = "timeout", ErrorMessage = "Gemini request timed out." };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini call failed");
            return new PartnerGenerateResult { Success = false, ErrorType = "unknown", ErrorMessage = ex.Message };
        }
    }

    private static PartnerGenerateResult MapError(HttpStatusCode status, string body)
    {
        var (type, message) = status switch
        {
            HttpStatusCode.TooManyRequests   => ("rate_limit",        TruncateError(body)),
            HttpStatusCode.RequestTimeout    => ("timeout",           "request timeout"),
            HttpStatusCode.Unauthorized      => ("auth_error",        TruncateError(body)),
            HttpStatusCode.Forbidden         => ("permission_error",  TruncateError(body)),
            HttpStatusCode.BadRequest        => DetectQuota(body),
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout    => ("server_error", TruncateError(body)),
            _ => ("unknown", TruncateError(body))
        };
        return new PartnerGenerateResult { Success = false, ErrorType = type, ErrorMessage = message, HttpStatus = (int)status };
    }

    private static (string, string) DetectQuota(string body)
    {
        if (body.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("quota", StringComparison.OrdinalIgnoreCase))
            return ("quota_exceeded", TruncateError(body));
        return ("bad_response", TruncateError(body));
    }

    private static string TruncateError(string body)
        => body.Length > 500 ? body[..500] : body;
}
