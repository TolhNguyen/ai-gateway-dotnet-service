using System.Security.Claims;

namespace AiGateway.Api.Infrastructure.Security;

public interface ICurrentUser
{
    long?   UserId   { get; }
    string? Email    { get; }
    string? Role     { get; }
    bool    IsAdmin  { get; }
    bool    IsAuthenticated { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public long? UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Principal?.FindFirstValue("sub");
            return long.TryParse(raw, out var v) ? v : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email)
                            ?? Principal?.FindFirstValue("email");

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public bool IsAdmin => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);

    public bool IsAuthenticated => UserId is > 0;
}
