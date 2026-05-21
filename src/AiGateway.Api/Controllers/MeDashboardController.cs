using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/me/dashboard")]
[Authorize]
public sealed class MeDashboardController : ControllerBase
{
    private readonly AiMetricsRepository _metrics;
    private readonly AiErrorRepository _errors;
    private readonly ICurrentUser _current;

    public MeDashboardController(AiMetricsRepository metrics, AiErrorRepository errors, ICurrentUser current)
    {
        _metrics = metrics;
        _errors = errors;
        _current = current;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var clamped = Math.Clamp(hours, 1, 24 * 30);
        var from = DateTimeOffset.UtcNow.AddHours(-clamped);
        var data = await _metrics.GetOverviewAsync(uid, from, ct);
        return Ok(new { hours = clamped, data });
    }

    [HttpGet("models")]
    public async Task<IActionResult> ByModel([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var from = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));
        var data = await _metrics.GetGroupedAsync(uid, "model", from, ct);
        return Ok(data);
    }

    [HttpGet("partners")]
    public async Task<IActionResult> ByPartner([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var from = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));
        var data = await _metrics.GetGroupedAsync(uid, "partner", from, ct);
        return Ok(data);
    }

    [HttpGet("account-keys")]
    public async Task<IActionResult> ByKey([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var from = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));
        var data = await _metrics.GetGroupedAsync(uid, "account_key", from, ct);
        return Ok(data);
    }

    [HttpGet("errors")]
    public async Task<IActionResult> Errors([FromQuery] int hours = 24, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var from = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));
        var data = await _errors.ListErrorsAsync(uid, from, Math.Clamp(limit, 1, 1000), ct);
        return Ok(data);
    }
}
