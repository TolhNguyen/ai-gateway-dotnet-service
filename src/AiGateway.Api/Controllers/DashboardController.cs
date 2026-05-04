using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Redis;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly AiMetricsRepository _metricsRepository;
    private readonly AiErrorRepository _errorRepository;
    private readonly AiConfigRepository _configRepository;
    private readonly RedisCooldownStore _cooldownStore;
    private readonly IDatabase _redis;

    public DashboardController(
        AiMetricsRepository metricsRepository,
        AiErrorRepository errorRepository,
        AiConfigRepository configRepository,
        RedisCooldownStore cooldownStore,
        IConnectionMultiplexer redis)
    {
        _metricsRepository = metricsRepository;
        _errorRepository = errorRepository;
        _configRepository = configRepository;
        _cooldownStore = cooldownStore;
        _redis = redis.GetDatabase();
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var range = NormalizeRange(from, to);
        return Ok(await _metricsRepository.GetOverviewAsync(range.From, range.To));
    }

    [HttpGet("models")]
    public async Task<IActionResult> Models([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var range = NormalizeRange(from, to);
        return Ok(await _metricsRepository.GetGroupedAsync("model", range.From, range.To));
    }

    [HttpGet("partners")]
    public async Task<IActionResult> Partners([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var range = NormalizeRange(from, to);
        return Ok(await _metricsRepository.GetGroupedAsync("partner", range.From, range.To));
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var range = NormalizeRange(from, to);
        return Ok(await _metricsRepository.GetGroupedAsync("account", range.From, range.To));
    }

    [HttpGet("clients")]
    public async Task<IActionResult> Clients([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var range = NormalizeRange(from, to);
        return Ok(await _metricsRepository.GetGroupedAsync("client", range.From, range.To));
    }

    [HttpGet("errors")]
    public async Task<IActionResult> Errors([FromQuery] int limit = 100)
    {
        return Ok(await _errorRepository.GetErrorsAsync(limit));
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var partners = await _configRepository.GetPartnersAsync();
        var result = new List<DashboardHealthPartnerDto>();

        foreach (var partner in partners)
        {
            var accounts = await _configRepository.GetAccountsByPartnerAsync(partner.Code);
            var accountHealth = new List<DashboardHealthAccountDto>();

            foreach (var account in accounts)
            {
                var cooldownInfo = await _cooldownStore.GetAccountCooldownAsync(account.Code);
                var anyCooldown = cooldownInfo is not null || await _cooldownStore.IsAccountCooldownAsync(account.Code);
                var inflight = await GetLongAsync(RedisKeys.InflightAccount(account.Code));
                var lastError = await _errorRepository.GetLastErrorForAccountAsync(account.Code);

                accountHealth.Add(new DashboardHealthAccountDto
                {
                    AccountCode = account.Code,
                    Status = account.Status,
                    Inflight = inflight,
                    Cooldown = anyCooldown,
                    CooldownReason = cooldownInfo?.Reason,
                    LastError = lastError
                });
            }

            result.Add(new DashboardHealthPartnerDto
            {
                PartnerCode = partner.Code,
                Status = partner.Status,
                Inflight = await GetLongAsync(RedisKeys.InflightPartner(partner.Code)),
                Cooldown = await _cooldownStore.IsPartnerCooldownAsync(partner.Code),
                Accounts = accountHealth
            });
        }

        return Ok(result);
    }

    private async Task<long> GetLongAsync(string key)
    {
        var value = await _redis.StringGetAsync(key);
        return value.HasValue && long.TryParse(value.ToString(), out var result) ? result : 0;
    }

    private static (DateTimeOffset From, DateTimeOffset To) NormalizeRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedTo = to ?? now;
        var normalizedFrom = from ?? normalizedTo.AddHours(-24);
        return (normalizedFrom, normalizedTo);
    }
}
