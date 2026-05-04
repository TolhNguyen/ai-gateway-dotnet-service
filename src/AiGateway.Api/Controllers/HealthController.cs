using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StackExchange.Redis;

namespace AiGateway.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IConnectionMultiplexer _redis;

    public HealthController(NpgsqlDataSource dataSource, IConnectionMultiplexer redis)
    {
        _dataSource = dataSource;
        _redis = redis;
    }

    [HttpGet("/health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "ok",
            service = "ai-gateway",
            at = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("/v1/debug/db/ping")]
    public async Task<IActionResult> PingDb()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        var result = await connection.ExecuteScalarAsync<int>("SELECT 1");
        return Ok(new { success = result == 1, db = "postgresql" });
    }

    [HttpGet("/v1/debug/redis/ping")]
    public async Task<IActionResult> PingRedis()
    {
        var db = _redis.GetDatabase();
        var ping = await db.PingAsync();
        return Ok(new { success = true, redis = "connected", pingMs = ping.TotalMilliseconds });
    }
}
