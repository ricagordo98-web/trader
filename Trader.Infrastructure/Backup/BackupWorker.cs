using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Maintenance;

namespace Trader.Infrastructure.Backup;

public sealed class BackupWorker(IServiceScopeFactory scopeFactory, IOptions<BackupOptions> options, ILogger<BackupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.IntervalHours <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(StartDelay, stoppingToken);

            using PeriodicTimer timer = new(CheckEvery);

            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IBackupService backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
            IReadOnlyList<BackupInfo> existing = await backups.ListAsync(cancellationToken);

            if (BackupSchedule.IsDue(existing.FirstOrDefault()?.CreatedAt, DateTimeOffset.UtcNow, TimeSpan.FromHours(options.Value.IntervalHours)))
            {
                await backups.CreateAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduled backup failed");
        }
    }
}
