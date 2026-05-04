using AiGateway.Api.Application;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Infrastructure.Security;

public sealed class AdminAuthMiddleware
{
    private static readonly PathString[] ProtectedPrefixes =
    [
        new PathString("/v1/admin"),
        new PathString("/v1/dashboard"),
        new PathString("/v1/debug")
    ];

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;
    private readonly AdminAuthOptions _options;
    private readonly ApiKeyHasher _apiKeyHasher;

    public AdminAuthMiddleware(
        RequestDelegate next,
        IHostEnvironment environment,
        IOptions<AdminAuthOptions> options,
        ApiKeyHasher apiKeyHasher)
    {
        _next = next;
        _environment = environment;
        _options = options.Value;
        _apiKeyHasher = apiKeyHasher;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsProtected(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        if (_environment.IsDevelopment() &&
            _options.AllowInDevelopmentWithoutKey &&
            string.IsNullOrWhiteSpace(_options.ApiKeyHash))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKeyHash))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "admin_auth_not_configured"
            });
            return;
        }

        var key = ExtractAdminKey(context.Request);
        if (string.IsNullOrWhiteSpace(key) || !_apiKeyHasher.Verify(key, _options.ApiKeyHash))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = "Bearer";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "admin_auth_required"
            });
            return;
        }

        await _next(context);
    }

    private static bool IsProtected(PathString path)
    {
        return ProtectedPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractAdminKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Admin-Key", out var headerKey))
        {
            return headerKey.ToString();
        }

        var auth = request.Headers["Authorization"].ToString();
        const string bearer = "Bearer ";
        if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return auth[bearer.Length..].Trim();
        }

        if (request.Query.TryGetValue("adminKey", out var queryKey))
        {
            return queryKey.ToString();
        }

        return null;
    }
}
