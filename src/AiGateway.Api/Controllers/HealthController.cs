using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StackExchange.Redis;

namespace AiGateway.Api.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly NpgsqlDataSource _ds;
    private readonly IConnectionMultiplexer _redis;

    public HealthController(NpgsqlDataSource ds, IConnectionMultiplexer redis)
    {
        _ds = ds;
        _redis = redis;
    }

    [HttpGet("/health")]
    public IActionResult Live() => Ok(new { status = "ok", time = DateTimeOffset.UtcNow });

    [HttpGet("/health/ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var pgOk = false; var redisOk = false;
        string? pgErr = null, redisErr = null;

        try
        {
            await using var conn = await _ds.OpenConnectionAsync(ct);
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            pgOk = true;
        }
        catch (Exception ex) { pgErr = ex.Message; }

        try
        {
            await _redis.GetDatabase().PingAsync();
            redisOk = true;
        }
        catch (Exception ex) { redisErr = ex.Message; }

        var ready = pgOk && redisOk;
        return StatusCode(ready ? 200 : 503, new
        {
            ready,
            postgres = new { ok = pgOk, error = pgErr },
            redis = new { ok = redisOk, error = redisErr },
            time = DateTimeOffset.UtcNow
        });
    }
}
