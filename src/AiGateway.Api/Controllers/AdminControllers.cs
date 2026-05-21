using AiGateway.Api.Application.Config;
using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using AiGateway.Api.Infrastructure.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/admin/partners")]
[Authorize(Roles = "admin")]
public sealed class AdminPartnersController : ControllerBase
{
    private readonly AiConfigRepository _repo;
    private readonly AiConfigService _cache;

    public AdminPartnersController(AiConfigRepository repo, AiConfigService cache) { _repo = repo; _cache = cache; }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _repo.ListPartnersAsync(ct));

    [HttpPut("{code}")]
    public async Task<IActionResult> Upsert(string code, [FromBody] UpsertPartnerRequest req, CancellationToken ct)
    {
        if (!string.Equals(code, req.Code, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "URL code does not match body code." });

        var p = new AiPartner {
            Code = req.Code, Name = req.Name, Status = req.Status,
            AdapterCode = req.AdapterCode, BaseUrl = req.BaseUrl,
            HealthCheckModel = req.HealthCheckModel,
            Weight = req.Weight, Priority = req.Priority, QualityScore = req.QualityScore
        };
        var saved = await _repo.UpsertPartnerAsync(p, ct);
        await _cache.InvalidatePartnerAsync(code);
        return Ok(saved);
    }

    [HttpPatch("{code}/status")]
    public async Task<IActionResult> Status(string code, [FromBody] UpdateStatusRequest req, CancellationToken ct)
    {
        var n = await _repo.UpdatePartnerStatusAsync(code, req.Status, ct);
        if (n == 0) return NotFound();
        await _cache.InvalidatePartnerAsync(code);
        return NoContent();
    }
}

[ApiController]
[Route("v1/admin/models")]
[Authorize(Roles = "admin")]
public sealed class AdminModelsController : ControllerBase
{
    private readonly AiConfigRepository _repo;
    private readonly AiConfigService _cache;

    public AdminModelsController(AiConfigRepository repo, AiConfigService cache) { _repo = repo; _cache = cache; }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _repo.ListModelsAsync(ct));

    [HttpPut("{code}")]
    public async Task<IActionResult> Upsert(string code, [FromBody] UpsertModelRequest req, CancellationToken ct)
    {
        if (!string.Equals(code, req.Code, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "URL code does not match body code." });

        var m = new AiModel {
            Code = req.Code, Name = req.Name, Status = req.Status,
            DefaultTemperature = req.DefaultTemperature, DefaultMaxTokens = req.DefaultMaxTokens,
            Strategy = req.Strategy, FallbackEnabled = req.FallbackEnabled, MaxRetry = req.MaxRetry
        };
        var saved = await _repo.UpsertModelAsync(m, ct);
        await _cache.InvalidateModelAsync(code);
        return Ok(saved);
    }

    [HttpPatch("{code}/status")]
    public async Task<IActionResult> Status(string code, [FromBody] UpdateStatusRequest req, CancellationToken ct)
    {
        var n = await _repo.UpdateModelStatusAsync(code, req.Status, ct);
        if (n == 0) return NotFound();
        await _cache.InvalidateModelAsync(code);
        return NoContent();
    }

    [HttpGet("{code}/routes")]
    public async Task<IActionResult> Routes(string code, CancellationToken ct)
        => Ok(await _repo.ListRoutesForModelAsync(code, ct));

    [HttpPut("{code}/routes")]
    public async Task<IActionResult> UpsertRoute(string code, [FromBody] UpsertRouteRequest req, CancellationToken ct)
    {
        var saved = await _repo.UpsertRouteAsync(
            code, req.PartnerCode, req.RouteCode, req.ProviderModel,
            req.Status, req.TimeoutMs, req.Weight, req.Priority, ct);
        return Ok(saved);
    }

    [HttpDelete("routes/{id:long}")]
    public async Task<IActionResult> DeleteRoute(long id, CancellationToken ct)
    {
        var n = await _repo.DeleteRouteAsync(id, ct);
        return n == 0 ? NotFound() : NoContent();
    }
}
