using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AiGateway.Api.Domain;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AiGateway.Api.Infrastructure.Security;

public sealed class JwtService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public JwtService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SecretBase64))
            throw new InvalidOperationException("Jwt:SecretBase64 is missing.");

        var keyBytes = Convert.FromBase64String(_options.SecretBase64);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:SecretBase64 must decode to at least 32 bytes.");

        var key = new SymmetricSecurityKey(keyBytes);
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.NameIdentifier
        };
    }

    public TokenValidationParameters ValidationParameters => _validationParameters;

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        var raw = new JwtSecurityTokenHandler().WriteToken(token);
        return (raw, expiresAt);
    }
}
