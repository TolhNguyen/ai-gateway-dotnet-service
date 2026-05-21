using AiGateway.Api.Application.HealthCheck;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Workers;

public sealed class HealthCheckWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiGatewayOptions _opts;
    private readonly ILogger<HealthCheckWorker> _logger;

    public HealthCheckWorker(IServiceScopeFactory scopeFactory, IOptions<AiGatewayOptions> opts, ILogger<HealthCheckWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First run: 30s after startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        var interval = TimeSpan.FromMinutes(_opts.HealthCheckIntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check sweep failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<AccountKeyRepository>();
        var service = scope.ServiceProvider.GetRequiredService<ApiKeyHealthCheckService>();

        var active = await repo.ListAllActiveAsync(ct);
        if (active.Count == 0) return;

        _logger.LogInformation("Health-checking {N} active user keys", active.Count);

        // Limit concurrency to be polite to providers.
        var semaphore = new SemaphoreSlim(4);
        var tasks = active.Select(async k =>
        {
            await semaphore.WaitAsync(ct);
            try { await service.CheckAsync(k, ct); }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);
    }
}
