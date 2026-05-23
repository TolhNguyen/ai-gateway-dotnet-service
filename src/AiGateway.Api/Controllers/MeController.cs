using AiGateway.Api.Application.Auth;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/me")]
[Authorize]
public sealed class MeController : ControllerBase
{
    private readonly UserRepository _users;
    private readonly AuthService _auth;
    private readonly ICurrentUser _current;
    private readonly AiConfigRepository _config;

    public MeController(UserRepository users, AuthService auth, ICurrentUser current, AiConfigRepository config)
    {
        _users = users;
        _auth = auth;
        _current = current;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var user = await _users.FindByIdAsync(uid, ct);
        if (user is null) return NotFound();
        return Ok(AuthService.ToDto(user));
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetAvailableModels(CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var rows = await _config.GetAvailableModelsForUserAsync(uid, ct);
        var dtos = rows.Select(r => new AvailableModelDto
        {
            Code = r.ModelCode,
            Name = r.ModelName,
            PartnerCode = r.PartnerCode,
            PartnerName = r.PartnerName,
            DefaultTemperature = r.DefaultTemperature,
            DefaultMaxTokens = r.DefaultMaxTokens
        }).ToList();
        return Ok(dtos);
    }

    [HttpGet("tokens")]
    public async Task<IActionResult> ListTokens(CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var list = await _auth.ListPatsAsync(uid, ct);
        return Ok(list);
    }

    [HttpPost("tokens")]
    public async Task<IActionResult> CreateToken([FromBody] CreatePatRequest req, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var resp = await _auth.CreatePatAsync(uid, req.Name, req.ExpiresInDays, ct);
        return Ok(resp);
    }

    [HttpDelete("tokens/{id:long}")]
    public async Task<IActionResult> RevokeToken(long id, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var n = await _auth.RevokePatAsync(uid, id, ct);
        return n == 0 ? NotFound() : NoContent();
    }
}
