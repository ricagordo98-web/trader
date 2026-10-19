using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;

namespace Trader.Infrastructure.Suggestions;

public sealed class SuggestionWorker(IServiceScopeFactory scopeFactory, ILogger<SuggestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

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
            await scope.ServiceProvider.GetRequiredService<ISuggestionService>().RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Suggestion cycle failed");
        }
    }
}
