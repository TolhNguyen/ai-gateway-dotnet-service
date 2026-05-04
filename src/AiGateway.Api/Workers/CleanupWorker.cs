using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Workers;

public sealed class CleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AiGatewayOptions _options;
    private readonly ILogger<CleanupWorker> _logger;

    public CleanupWorker(
        IServiceProvider serviceProvider,
        IOptions<AiGatewayOptions> options,
        ILogger<CleanupWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<AiErrorRepository>();
                var days = _options.ErrorEventsRetentionDays;
                await repository.CleanupErrorEventsAsync(days, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup worker failed");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
