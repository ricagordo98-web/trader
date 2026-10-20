using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Summary;

namespace Trader.Infrastructure.Summary;

public static class SummarySchedule
{
    public static bool IsDue(DateTimeOffset? lastGenerated, DateTimeOffset now, int generateHour) =>
        now.LocalDateTime.Hour >= generateHour && (lastGenerated is null || lastGenerated.Value.LocalDateTime.Date < now.LocalDateTime.Date);
}

public sealed class SummaryWorker(IServiceScopeFactory scopeFactory, IOptions<SummaryOptions> options, ILogger<SummaryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            IDailySummaryService service = scope.ServiceProvider.GetRequiredService<IDailySummaryService>();
            DailySummary? last = await service.GetLastAsync(cancellationToken);

            if (!SummarySchedule.IsDue(last?.GeneratedAt, DateTimeOffset.UtcNow, options.Value.GenerateHour))
            {
                return;
            }

            DailySummary summary = await service.GenerateAsync(cancellationToken);
            string message = summary.Narrative ?? summary.Headline;

            await scope.ServiceProvider.GetRequiredService<IAlertService>().RaiseAsync(
                [new Alert
                {
                    Kind = AlertKind.DailySummary,
                    Symbol = "RESUMO",
                    Title = "Resumo do dia",
                    Message = message.Length > 240 ? message[..237] + "…" : message,
                    CreatedAt = DateTimeOffset.UtcNow
                }],
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Daily summary cycle failed");
        }
    }
}
