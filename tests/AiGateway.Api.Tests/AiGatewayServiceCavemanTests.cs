using System.Security.Claims;
using AiGateway.Api.Application.AI;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AiGateway.Api.Tests;

public sealed class AiGatewayServiceCavemanTests
{
    [Fact]
    public void BuildEffectiveSystemPrompt_DoesNotApplyCavemanForJwt()
    {
        var http = HttpWithClaims(
            new Claim("auth_method", "jwt"),
            new Claim("use_caveman", "true"));

        var result = AiGatewayService.BuildEffectiveSystemPrompt("Be precise.", http);

        Assert.Equal("Be precise.", result);
    }

    [Fact]
    public void BuildEffectiveSystemPrompt_AppendsCavemanInstructionForPat()
    {
        var http = HttpWithClaims(
            new Claim("auth_method", "pat"),
            new Claim("use_caveman", "true"));

        var result = AiGatewayService.BuildEffectiveSystemPrompt("Be precise.", http);

        Assert.StartsWith("Be precise.", result);
        Assert.Contains("Respond in Caveman engineering style.", result);
        Assert.Contains("Use few words.", result);
    }

    [Fact]
    public void BuildEffectiveSystemPrompt_UsesCavemanInstructionWhenSystemPromptIsEmpty()
    {
        var http = HttpWithClaims(
            new Claim("auth_method", "pat"),
            new Claim("use_caveman", "true"));

        var result = AiGatewayService.BuildEffectiveSystemPrompt(null, http);

        Assert.StartsWith("Respond in Caveman engineering style.", result);
        Assert.DoesNotContain("\n\n\n", result);
    }

    private static HttpContext HttpWithClaims(params Claim[] claims)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return http;
    }
}
