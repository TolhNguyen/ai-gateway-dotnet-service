using System.Security.Claims;
using AiGateway.Api.Application.Auth;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Security;
using Xunit;

namespace AiGateway.Api.Tests;

public sealed class PatResponseStyleTests
{
    [Fact]
    public void ToPatDto_MapsCavemanResponseStyle()
    {
        var pat = new PersonalAccessToken
        {
            Id = 42,
            UserId = 7,
            Name = "integration",
            TokenPrefix = "aigw_abc123",
            ResponseStyle = "caveman",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var dto = AuthService.ToPatDto(pat);

        Assert.True(dto.UseCaveman);
        Assert.Equal("caveman", dto.ResponseStyle);
    }

    [Fact]
    public void BuildClaims_IncludesPatResponseStyle()
    {
        var user = new User
        {
            Id = 7,
            Email = "user@example.com",
            Role = "user",
            Status = "active"
        };
        var pat = new PersonalAccessToken
        {
            Id = 42,
            UserId = user.Id,
            Name = "integration",
            ResponseStyle = "caveman"
        };

        var claims = PatAuthenticationHandler.BuildClaims(user, pat).ToArray();

        Assert.Contains(claims, c => c.Type == "auth_method" && c.Value == "pat");
        Assert.Contains(claims, c => c.Type == "pat_id" && c.Value == "42");
        Assert.Contains(claims, c => c.Type == "response_style" && c.Value == "caveman");
        Assert.Contains(claims, c => c.Type == "use_caveman" && c.Value == "true");
        Assert.Contains(claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == "7");
    }
}
