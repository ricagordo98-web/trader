using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;

namespace Trader.Infrastructure.MarketData;

public sealed class QuoteWarmupWorker(IServiceScopeFactory scopeFactory, ILogger<QuoteWarmupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(90);

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
            await scope.ServiceProvider.GetRequiredService<IMarketDataService>().GetQuotesAsync(null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Quote warm-up failed: {Message}", ex.Message);
        }
    }
}
