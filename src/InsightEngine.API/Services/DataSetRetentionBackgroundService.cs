using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Options;

namespace InsightEngine.API.Services;

public class DataSetRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InsightEngineSettings _settings;
    private readonly ILogger<DataSetRetentionBackgroundService> _logger;

    public DataSetRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<InsightEngineSettings> settings,
        ILogger<DataSetRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Max(_settings.CleanupIntervalMinutes, 1);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        await ExecuteCleanupAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteCleanupAsync(stoppingToken);
        }
    }

    private async Task ExecuteCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var cleanupService = scope.ServiceProvider.GetRequiredService<IDataSetCleanupService>();
            await cleanupService.CleanupExpiredAsync(_settings.RetentionDays, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataset retention cleanup execution failed.");
        }
    }
}
