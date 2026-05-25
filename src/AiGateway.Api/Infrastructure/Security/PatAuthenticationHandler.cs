using System.Security.Claims;
using System.Text.Encodings.Web;
using AiGateway.Api.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Infrastructure.Security;

public sealed class PatAuthenticationOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Authentication handler for Personal Access Tokens (PATs).
/// Tokens are passed via:
///   Authorization: Bearer aigw_xxx
/// JWTs and PATs share the same header; this handler is consulted when JWT validation fails.
/// </summary>
public sealed class PatAuthenticationHandler : AuthenticationHandler<PatAuthenticationOptions>
{
    public const string SchemeName = "PAT";
    private const string Prefix = "aigw_";

    private readonly UserRepository _userRepo;
    private readonly TokenHasher _tokenHasher;

    public PatAuthenticationHandler(
        IOptionsMonitor<PatAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        UserRepository userRepo,
        TokenHasher tokenHasher)
        : base(options, logger, encoder)
    {
        _userRepo = userRepo;
        _tokenHasher = tokenHasher;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = auth["Bearer ".Length..].Trim();

        // Only handle aigw_ prefixed tokens; let JWT scheme handle the rest.
        if (!token.StartsWith(Prefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var hash = _tokenHasher.Hash(token);
        var pat = await _userRepo.FindActivePatByHashAsync(hash, Context.RequestAborted);
        if (pat is null)
            return AuthenticateResult.Fail("Invalid or expired access token.");

        var user = await _userRepo.FindByIdAsync(pat.UserId, Context.RequestAborted);
        if (user is null || !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("User disabled.");

        // Touch last_used_at fire-and-forget — failures here shouldn't block the request.
        _ = _userRepo.TouchPatLastUsedAsync(pat.Id);

        var claims = BuildClaims(user, pat);

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    internal static IEnumerable<Claim> BuildClaims(AiGateway.Api.Domain.User user, AiGateway.Api.Domain.PersonalAccessToken pat)
    {
        return new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("auth_method", "pat"),
            new Claim("pat_id", pat.Id.ToString()),
            new Claim("response_style", pat.ResponseStyle),
            new Claim("use_caveman", pat.UseCaveman ? "true" : "false")
        };
    }
}
