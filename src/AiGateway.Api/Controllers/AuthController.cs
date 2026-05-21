using AiGateway.Api.Application.Auth;
using AiGateway.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) { _auth = auth; }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var (ok, error, user) = await _auth.RegisterAsync(req.Email, req.Password, req.DisplayName, ct);
        if (!ok) return BadRequest(new { error });
        return Ok(AuthService.ToDto(user!));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var (ok, error, resp) = await _auth.LoginAsync(req.Email, req.Password, ct);
        if (!ok) return Unauthorized(new { error });
        return Ok(resp);
    }
}
