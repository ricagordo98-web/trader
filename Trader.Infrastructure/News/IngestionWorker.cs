using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News;

public sealed class IngestionWorker(
    IEnumerable<ISourceConnector> connectors,
    IServiceScopeFactory scopeFactory,
    ISourceStatusRegistry statusRegistry,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ISourceConnector[] enabled = connectors.Where(c => c.IsEnabled).ToArray();

        foreach (ISourceConnector connector in connectors)
        {
            statusRegistry.Report(new SourceStatus(
                connector.Name,
                connector.IsEnabled,
                connector.IsEnabled ? null : connector.DisabledReason ?? "Desativada.",
                (int)connector.Interval.TotalMinutes,
                null,
                0,
                0,
                null));

            if (!connector.IsEnabled)
            {
                logger.LogInformation("Source {Source} is disabled: {Reason}", connector.Name, connector.DisabledReason);
            }
        }

        await Task.WhenAll(enabled.Select((connector, index) => RunConnectorAsync(connector, TimeSpan.FromSeconds(3 * index), stoppingToken)));
    }

    private async Task RunConnectorAsync(ISourceConnector connector, TimeSpan startDelay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(startDelay, stoppingToken);

            using PeriodicTimer timer = new(connector.Interval);

            do
            {
                await FetchOnceAsync(connector, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FetchOnceAsync(ISourceConnector connector, CancellationToken cancellationToken)
    {
        int fetched = 0;
        int added = 0;
        string? error = null;

        try
        {
            IReadOnlyList<RawArticle> raw = await connector.FetchAsync(cancellationToken);
            fetched = raw.Count;

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            added = await scope.ServiceProvider.GetRequiredService<ArticleIngestor>().IngestAsync(raw, cancellationToken);

            logger.LogInformation("{Source}: fetched {Fetched}, new {Added}", connector.Name, fetched, added);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            logger.LogError(ex, "Ingestion failed for {Source}", connector.Name);
        }

        statusRegistry.Report(new SourceStatus(
            connector.Name,
            true,
            null,
            (int)connector.Interval.TotalMinutes,
            DateTimeOffset.UtcNow,
            fetched,
            added,
            error ?? (fetched == 0 ? "A fonte não devolveu artigos (limite de pedidos ou indisponível)." : null)));
    }
}
