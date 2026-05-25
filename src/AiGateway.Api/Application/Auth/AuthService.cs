using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Security;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Application.Auth;

public sealed class AuthService
{
    private readonly UserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly JwtService _jwt;
    private readonly TokenHasher _tokenHasher;
    private readonly JwtOptions _jwtOpts;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserRepository users,
        IPasswordHasher hasher,
        JwtService jwt,
        TokenHasher tokenHasher,
        IOptions<JwtOptions> jwtOpts,
        ILogger<AuthService> logger)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
        _tokenHasher = tokenHasher;
        _jwtOpts = jwtOpts.Value;
        _logger = logger;
    }

    public async Task<(bool ok, string? error, User? user)> RegisterAsync(
        string email, string password, string? displayName, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();

        var existing = await _users.FindByEmailAsync(email, ct);
        if (existing is not null) return (false, "Email already registered.", null);

        var hash = _hasher.Hash(password);
        var user = await _users.CreateAsync(email, hash, role: "user", displayName, ct);
        _logger.LogInformation("Registered new user {Email} (id={Id})", email, user.Id);
        return (true, null, user);
    }

    public async Task<(bool ok, string? error, LoginResponse? response)> LoginAsync(
        string email, string password, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(email, ct);
        if (user is null) return (false, "Invalid credentials.", null);
        if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
            return (false, "Account disabled.", null);

        if (!_hasher.Verify(password, user.PasswordHash))
            return (false, "Invalid credentials.", null);

        var (token, expiresAt) = _jwt.Issue(user);

        return (true, null, new LoginResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresInSeconds = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds,
            User = ToDto(user)
        });
    }

    public async Task<CreatePatResponse> CreatePatAsync(
        long userId, string name, int? expiresInDays, bool useCaveman, CancellationToken ct)
    {
        var raw = _tokenHasher.Generate("aigw");
        var hash = _tokenHasher.Hash(raw);
        var prefix = raw.Length > 12 ? raw[..12] : raw;
        var responseStyle = useCaveman ? "caveman" : "normal";

        DateTimeOffset? expiresAt = expiresInDays is > 0
            ? DateTimeOffset.UtcNow.AddDays(expiresInDays.Value)
            : null;

        var pat = await _users.CreatePatAsync(userId, name, hash, prefix, expiresAt, responseStyle, ct);

        return new CreatePatResponse
        {
            Id = pat.Id,
            Name = pat.Name,
            Token = raw,                  // returned only at creation
            ExpiresAt = pat.ExpiresAt,
            CreatedAt = pat.CreatedAt,
            UseCaveman = pat.UseCaveman,
            ResponseStyle = pat.ResponseStyle
        };
    }

    public async Task<IReadOnlyList<PatDto>> ListPatsAsync(long userId, CancellationToken ct)
    {
        var list = await _users.ListPatsForUserAsync(userId, ct);
        return list.Select(ToPatDto).ToList();
    }

    public Task<int> RevokePatAsync(long userId, long patId, CancellationToken ct)
        => _users.RevokePatAsync(userId, patId, ct);

    public static UserDto ToDto(User u) => new()
    {
        Id = u.Id, Email = u.Email, Role = u.Role, Status = u.Status,
        DisplayName = u.DisplayName, CreatedAt = u.CreatedAt
    };

    public static PatDto ToPatDto(PersonalAccessToken p) => new()
    {
        Id = p.Id, Name = p.Name, TokenPrefix = p.TokenPrefix,
        LastUsedAt = p.LastUsedAt, ExpiresAt = p.ExpiresAt, CreatedAt = p.CreatedAt,
        UseCaveman = p.UseCaveman, ResponseStyle = p.ResponseStyle
    };
}
