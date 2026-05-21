namespace AiGateway.Api.Infrastructure.Http;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Request-Id";
    private const string Property = "RequestId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var rid = ctx.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rid) || rid.Length > 128)
            rid = Guid.NewGuid().ToString("N");

        ctx.Response.Headers[HeaderName] = rid;
        ctx.Items[Property] = rid;

        using (_logger.BeginScope(new Dictionary<string, object> { [Property] = rid }))
            await _next(ctx);
    }
}
