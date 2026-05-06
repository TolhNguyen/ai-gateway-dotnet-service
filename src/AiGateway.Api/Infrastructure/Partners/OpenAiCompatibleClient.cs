using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiGateway.Api.Contracts;

namespace AiGateway.Api.Infrastructure.Partners;

public sealed class OpenAiCompatibleClient : IAiPartnerClient
{
    public string AdapterCode => "openai_compatible";

    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiCompatibleClient(IHttpClientFactory httpClientFactory)
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
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

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

            string? content = null;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
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
                    ErrorMessage = "OpenAI-compatible response missing choices[0].message.content",
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
            lower.Contains("too many requests") ||
            lower.Contains("quota"))
        {
            var scope = DetectScope(message);
            return new PartnerGenerateResult
            {
                Success = false,
                HttpStatus = status,
                ErrorType = lower.Contains("quota") ? "quota_exceeded" : "rate_limit",
                ErrorCode = info.Code,
                ErrorMessage = message,
                Retryable = true,
                RetryAfterSeconds = RetryAfterSeconds(retryAfter),
                LimitScope = scope,
                SuggestedCooldownSeconds = SuggestedCooldownSeconds(scope, retryAfter),
                Raw = raw
            };
        }

        if (status >= 500)
        {
            return new PartnerGenerateResult
            {
                Success = false,
                HttpStatus = status,
                ErrorType = "server_error",
                ErrorCode = info.Code,
                ErrorMessage = message,
                Retryable = true,
                Raw = raw
            };
        }

        if (status == 401)
        {
            return new PartnerGenerateResult
            {
                Success = false,
                HttpStatus = status,
                ErrorType = "auth_error",
                ErrorCode = info.Code,
                ErrorMessage = message,
                Retryable = false,
                Raw = raw
            };
        }

        if (status == 403)
        {
            return new PartnerGenerateResult
            {
                Success = false,
                HttpStatus = status,
                ErrorType = "permission_error",
                ErrorCode = info.Code,
                ErrorMessage = message,
                Retryable = false,
                Raw = raw
            };
        }

        if (status == 400)
        {
            return new PartnerGenerateResult
            {
                Success = false,
                HttpStatus = status,
                ErrorType = "validation_error",
                ErrorCode = info.Code,
                ErrorMessage = message,
                Retryable = false,
                Raw = raw
            };
        }

        return new PartnerGenerateResult
        {
            Success = false,
            HttpStatus = status,
            ErrorType = "provider_error",
            ErrorCode = info.Code,
            ErrorMessage = message,
            Retryable = false,
            Raw = raw
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
                    var message = error.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                    var code = error.TryGetProperty("code", out var codeNode) ? codeNode.ToString() : null;
                    return new ErrorInfo(message ?? code ?? raw, code);
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return new ErrorInfo(error.GetString() ?? raw, null);
                }
            }

            if (root.TryGetProperty("message", out var rootMessage)) return new ErrorInfo(rootMessage.GetString() ?? raw, null);
            return new ErrorInfo(raw, null);
        }
        catch
        {
            return new ErrorInfo(raw, null);
        }
    }

    private static string DetectScope(string message)
    {
        var lower = message.ToLowerInvariant();
        if (lower.Contains("tokens per minute") || lower.Contains("tpm")) return "tpm";
        if (lower.Contains("minute") || lower.Contains("rpm")) return "rpm";
        if (lower.Contains("daily") || lower.Contains("per day") || lower.Contains("day")) return "daily";
        if (lower.Contains("monthly") || lower.Contains("month")) return "monthly";
        return "unknown";
    }

    private static int? RetryAfterSeconds(TimeSpan? retryAfter)
        => retryAfter.HasValue ? Math.Max(1, Convert.ToInt32(Math.Ceiling(retryAfter.Value.TotalSeconds))) : null;

    private static int SuggestedCooldownSeconds(string scope, TimeSpan? retryAfter)
    {
        var retry = RetryAfterSeconds(retryAfter);
        if (retry is > 0) return retry.Value;

        return scope switch
        {
            "rpm" or "tpm" => 60,
            "daily" => 86400,
            "monthly" => 604800,
            _ => 300
        };
    }

    private static int? GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var node) && node.TryGetInt32(out var value) ? value : null;

    private sealed record ErrorInfo(string Message, string? Code);
}
