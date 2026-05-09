using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiGateway.Api.Contracts;

namespace AiGateway.Api.Infrastructure.Partners;

/// <summary>
/// OpenRouter client. OpenRouter yêu cầu thêm 2 header:
///   HTTP-Referer: URL app của bạn (bắt buộc để không bị block)
///   X-Title:      Tên app (optional nhưng nên có)
///
/// Đăng ký trong Program.cs:
///   builder.Services.AddScoped&lt;IAiPartnerClient, OpenRouterClient&gt;();
///
/// Tạo partner qua admin API với adapterCode = "openrouter":
///   POST /v1/admin/partners
///   { "code": "openrouter", "adapterCode": "openrouter", "baseUrl": "https://openrouter.ai/api", ... }
///
/// Model free tier dùng suffix :free, ví dụ:
///   "mistralai/mistral-7b-instruct:free"
///   "meta-llama/llama-3.2-3b-instruct:free"
///   "google/gemma-3-27b-it:free"
/// Xem danh sách: https://openrouter.ai/models?order=newest&supported_parameters=free
/// </summary>
public sealed class OpenRouterClient : IAiPartnerClient
{
    public string AdapterCode => "openrouter";

    // Đổi thành URL production thật của bạn khi deploy.
    // OpenRouter dùng giá trị này để group usage trên dashboard của họ.
    private const string AppReferer = "https://github.com/your-org/ai-gateway";
    private const string AppTitle = "AI Gateway";

    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PartnerGenerateResult> GenerateAsync(
        PartnerCallContext context,
        PartnerGenerateRequest request,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("ai-partners");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(context.TimeoutMs));

        var url = $"{context.BaseUrl.TrimEnd('/')}/v1/chat/completions";

        var body = new
        {
            model = context.ProviderModel,
            messages = request.Messages.Select(x => new { role = x.Role, content = x.Content }),
            temperature = request.Temperature,
            max_tokens = request.MaxTokens
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.ApiKey);
        httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", AppReferer);
        httpRequest.Headers.TryAddWithoutValidation("X-Title", AppTitle);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, timeoutCts.Token);
            var raw = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return ParseError(raw, (int)response.StatusCode, response.Headers.RetryAfter?.Delta);
            }

            return ParseSuccess(raw);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new PartnerGenerateResult
            {
                Success = false,
                ErrorType = "timeout",
                ErrorMessage = ex.Message,
                Retryable = true
            };
        }
        catch (Exception ex)
        {
            return new PartnerGenerateResult
            {
                Success = false,
                ErrorType = "unknown",
                ErrorMessage = ex.Message,
                Retryable = false
            };
        }
    }

    private static PartnerGenerateResult ParseSuccess(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // OpenRouter trả error inline trong body ngay cả khi HTTP 200
            // khi model bị moderation chặn hoặc upstream hết quota.
            if (root.TryGetProperty("error", out var inlineError))
            {
                var errMsg = inlineError.TryGetProperty("message", out var em) ? em.GetString() : raw;
                var errCode = inlineError.TryGetProperty("code", out var ec) ? ec.ToString() : null;
                var lower = errMsg?.ToLowerInvariant() ?? string.Empty;

                var errorType = lower.Contains("rate") ? "rate_limit"
                    : lower.Contains("quota") || lower.Contains("credit") ? "quota_exceeded"
                    : lower.Contains("moderation") || lower.Contains("content policy") ? "permission_error"
                    : "provider_error";

                return new PartnerGenerateResult
                {
                    Success = false,
                    ErrorType = errorType,
                    ErrorCode = errCode,
                    ErrorMessage = errMsg,
                    Retryable = errorType is "rate_limit" or "quota_exceeded",
                    Raw = raw
                };
            }

            string? content = null;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];

                // :free models đôi khi trả finish_reason = "error"
                if (first.TryGetProperty("finish_reason", out var fr) && fr.GetString() == "error")
                {
                    return new PartnerGenerateResult
                    {
                        Success = false,
                        ErrorType = "provider_error",
                        ErrorMessage = "OpenRouter upstream model returned finish_reason=error",
                        Retryable = true,
                        Raw = raw
                    };
                }

                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentNode))
                {
                    content = contentNode.ValueKind == JsonValueKind.String
                        ? contentNode.GetString()
                        : contentNode.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return new PartnerGenerateResult
                {
                    Success = false,
                    ErrorType = "bad_response",
                    ErrorMessage = "OpenRouter response missing choices[0].message.content",
                    Retryable = true,
                    Raw = raw
                };
            }

            AiUsageDto? usage = null;
            if (root.TryGetProperty("usage", out var usageNode))
            {
                usage = new AiUsageDto
                {
                    InputTokens = GetInt(usageNode, "prompt_tokens"),
                    OutputTokens = GetInt(usageNode, "completion_tokens"),
                    TotalTokens = GetInt(usageNode, "total_tokens")
                };
            }

            return new PartnerGenerateResult
            {
                Success = true,
                Content = content,
                Usage = usage,
                Raw = JsonSerializer.Deserialize<object>(raw, JsonOptions)
            };
        }
        catch (Exception ex)
        {
            return new PartnerGenerateResult
            {
                Success = false,
                ErrorType = "bad_response",
                ErrorMessage = ex.Message,
                Retryable = true,
                Raw = raw
            };
        }
    }

    private static PartnerGenerateResult ParseError(string raw, int status, TimeSpan? retryAfter)
    {
        var info = ExtractErrorInfo(raw);
        var message = info.Message;
        var lower = message.ToLowerInvariant();

        if (status == (int)HttpStatusCode.TooManyRequests ||
            lower.Contains("rate limit") ||
            lower.Contains("too many requests"))
        {
            var scope = lower.Contains("daily") || lower.Contains("credit") ? "daily" : "rpm";
            return new PartnerGenerateResult
            {
                Success = false, HttpStatus = status, ErrorType = "rate_limit",
                ErrorCode = info.Code, ErrorMessage = message, Retryable = true,
                RetryAfterSeconds = RetryAfterSeconds(retryAfter),
                LimitScope = scope,
                SuggestedCooldownSeconds = SuggestedCooldownSeconds(scope, retryAfter),
                Raw = raw
            };
        }

        if (lower.Contains("quota") || lower.Contains("limit exceeded") || lower.Contains("insufficient credit"))
        {
            return new PartnerGenerateResult
            {
                Success = false, HttpStatus = status, ErrorType = "quota_exceeded",
                ErrorCode = info.Code, ErrorMessage = message, Retryable = true,
                LimitScope = "daily", SuggestedCooldownSeconds = 86400, Raw = raw
            };
        }

        if (status >= 500)
            return new PartnerGenerateResult
            {
                Success = false, HttpStatus = status, ErrorType = "server_error",
                ErrorCode = info.Code, ErrorMessage = message, Retryable = true, Raw = raw
            };

        if (status == 401)
            return new PartnerGenerateResult
            {
                Success = false, HttpStatus = status, ErrorType = "auth_error",
                ErrorCode = info.Code, ErrorMessage = message, Retryable = false, Raw = raw
            };

        if (status == 403)
            return new PartnerGenerateResult
            {
                Success = false, HttpStatus = status, ErrorType = "permission_error",
                ErrorCode = info.Code, ErrorMessage = message, Retryable = false, Raw = raw
            };

        return new PartnerGenerateResult
        {
            Success = false, HttpStatus = status, ErrorType = "provider_error",
            ErrorCode = info.Code, ErrorMessage = message, Retryable = false, Raw = raw
        };
    }

    private static ErrorInfo ExtractErrorInfo(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new ErrorInfo(string.Empty, null);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    var msg = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                    var code = error.TryGetProperty("code", out var c) ? c.ToString() : null;
                    return new ErrorInfo(msg ?? raw, code);
                }
                if (error.ValueKind == JsonValueKind.String) return new ErrorInfo(error.GetString() ?? raw, null);
            }
            if (root.TryGetProperty("message", out var rootMsg)) return new ErrorInfo(rootMsg.GetString() ?? raw, null);
            return new ErrorInfo(raw, null);
        }
        catch { return new ErrorInfo(raw, null); }
    }

    private static int? RetryAfterSeconds(TimeSpan? retryAfter)
        => retryAfter.HasValue ? Math.Max(1, Convert.ToInt32(Math.Ceiling(retryAfter.Value.TotalSeconds))) : null;

    private static int SuggestedCooldownSeconds(string scope, TimeSpan? retryAfter)
    {
        var retry = RetryAfterSeconds(retryAfter);
        if (retry is > 0) return retry.Value;
        return scope switch { "rpm" or "tpm" => 60, "daily" => 86400, _ => 300 };
    }

    private static int? GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var node) && node.TryGetInt32(out var value) ? value : null;

    private sealed record ErrorInfo(string Message, string? Code);
}
