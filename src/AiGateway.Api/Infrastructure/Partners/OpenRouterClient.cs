using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Infrastructure.Partners;

public sealed class OpenRouterClient : IAiPartnerClient
{
    public string AdapterCode => "openrouter";

    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<OpenRouterClient> _logger;

    public OpenRouterClient(IHttpClientFactory factory, IOptions<OpenRouterOptions> options, ILogger<OpenRouterClient> logger)
    {
        _httpFactory = factory;
        _options = options.Value;
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
            // OpenRouter requires identifying app via HTTP-Referer + X-Title.
            if (!string.IsNullOrEmpty(_options.AppReferer))
                msg.Headers.TryAddWithoutValidation("HTTP-Referer", _options.AppReferer);
            if (!string.IsNullOrEmpty(_options.AppTitle))
                msg.Headers.TryAddWithoutValidation("X-Title", _options.AppTitle);

            msg.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(msg, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return OpenAiCompatibleClient.MapError(resp.StatusCode, raw);

            return OpenAiCompatibleClient.ParseChatCompletion(raw);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PartnerGenerateResult { Success = false, ErrorType = "timeout", ErrorMessage = "OpenRouter request timed out." };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenRouter call failed");
            return new PartnerGenerateResult { Success = false, ErrorType = "unknown", ErrorMessage = ex.Message };
        }
    }
}
