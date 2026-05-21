using AiGateway.Api.Application.Config;
using AiGateway.Api.Application.HealthCheck;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/me/keys")]
[Authorize]
public sealed class MeKeysController : ControllerBase
{
    private readonly AccountKeyService _service;
    private readonly ApiKeyHealthCheckService _healthService;
    private readonly RedisRateLimitStore _rateStore;
    private readonly RedisCooldownStore _cooldownStore;
    private readonly ICurrentUser _current;

    public MeKeysController(
        AccountKeyService service,
        ApiKeyHealthCheckService healthService,
        RedisRateLimitStore rateStore,
        RedisCooldownStore cooldownStore,
        ICurrentUser current)
    {
        _service = service;
        _healthService = healthService;
        _rateStore = rateStore;
        _cooldownStore = cooldownStore;
        _current = current;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        return Ok(await _service.ListAsync(uid, ct));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var dto = await _service.GetAsync(uid, id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountKeyRequest req, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var (ok, error, dto) = await _service.CreateAsync(uid, req, ct);
        if (!ok) return BadRequest(new { error });
        return CreatedAtAction(nameof(Get), new { id = dto!.Id }, dto);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAccountKeyRequest req, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var (ok, error, dto) = await _service.UpdateAsync(uid, id, req, ct);
        if (!ok) return error == "Not found." ? NotFound() : BadRequest(new { error });
        return Ok(dto);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var n = await _service.DeleteAsync(uid, id, ct);
        return n == 0 ? NotFound() : NoContent();
    }

    [HttpPost("{id:long}/health-check")]
    public async Task<IActionResult> RunHealthCheck(long id, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var key = await _service.GetEntityAsync(uid, id, ct);
        if (key is null) return NotFound();

        var (status, error, latency) = await _healthService.CheckAsync(key, ct);
        return Ok(new
        {
            id,
            status,
            error,
            latencyMs = latency,
            checkedAt = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("health")]
    public async Task<IActionResult> HealthOverview(CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();
        var list = await _service.ListAsync(uid, ct);

        var result = new List<AccountKeyHealthDto>();
        foreach (var k in list)
        {
            var inflight = 0L;
            var cooldown = false;
            if (await _service.GetEntityAsync(uid, k.Id, ct) is { } entity)
            {
                inflight = await _rateStore.GetInflightForKeyAsync(entity.Id);
                cooldown = await _cooldownStore.IsAccountKeyCooldownAsync(entity.Id);
            }
            result.Add(new AccountKeyHealthDto
            {
                Id = k.Id, Code = k.Code, PartnerCode = k.PartnerCode, Status = k.Status,
                LastHealthStatus = k.LastHealthStatus, LastHealthError = k.LastHealthError,
                LastHealthLatencyMs = k.LastHealthLatencyMs, LastHealthCheckAt = k.LastHealthCheckAt,
                Inflight = inflight, Cooldown = cooldown
            });
        }
        return Ok(result);
    }
}
