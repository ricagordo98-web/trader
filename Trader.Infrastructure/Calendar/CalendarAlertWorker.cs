using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Calendar;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Calendar;

public sealed class CalendarAlertWorker(IServiceScopeFactory scopeFactory, ILogger<CalendarAlertWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);

            using PeriodicTimer timer = new(Interval);

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
            CalendarFeed feed = await scope.ServiceProvider.GetRequiredService<ICalendarService>().GetAsync(3, cancellationToken);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            IReadOnlyList<Alert> candidates = CalendarAlerts.ForTomorrow(feed.Events, DateOnly.FromDateTime(now.UtcDateTime), now);

            if (candidates.Count == 0)
            {
                return;
            }

            TraderDbContext dbContext = scope.ServiceProvider.GetRequiredService<TraderDbContext>();
            DateTimeOffset since = now.AddHours(-36);
            List<string> recentTitles = await dbContext.Alerts
                .Where(a => a.Kind == AlertKind.Calendar && a.CreatedAt >= since)
                .Select(a => a.Title)
                .ToListAsync(cancellationToken);

            List<Alert> fresh = candidates.Where(a => !recentTitles.Contains(a.Title)).ToList();

            if (fresh.Count > 0)
            {
                await scope.ServiceProvider.GetRequiredService<IAlertService>().RaiseAsync(fresh, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Calendar alert cycle failed");
        }
    }
}
