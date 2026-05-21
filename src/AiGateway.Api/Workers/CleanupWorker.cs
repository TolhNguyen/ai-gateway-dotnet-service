using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Workers;

public sealed class CleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiGatewayOptions _opts;
    private readonly ILogger<CleanupWorker> _logger;

    public CleanupWorker(IServiceScopeFactory scopeFactory, IOptions<AiGatewayOptions> opts, ILogger<CleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run hourly.
        var interval = TimeSpan.FromHours(1);
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<AiErrorRepository>();
                var cutoff = DateTimeOffset.UtcNow.AddDays(-_opts.ErrorEventsRetentionDays);
                var n = await repo.DeleteOldEventsAsync(cutoff, stoppingToken);
                if (n > 0) _logger.LogInformation("Cleaned up {N} old error events (before {Cutoff})", n, cutoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup iteration failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
