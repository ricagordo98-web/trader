using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Journal;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Alerts;

public sealed class PriceWatchWorker(IServiceScopeFactory scopeFactory, ILogger<PriceWatchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);

            using PeriodicTimer timer = new(Interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
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
            TraderDbContext dbContext = scope.ServiceProvider.GetRequiredService<TraderDbContext>();

            List<JournalTrade> trades = await dbContext.JournalTrades
                .Where(t => t.Status == TradeStatus.Open && (t.StopLoss != null || t.TakeProfit != null))
                .ToListAsync(cancellationToken);

            List<PriceAlert> priceAlerts = await dbContext.PriceAlerts
                .Where(a => a.TriggeredAt == null)
                .ToListAsync(cancellationToken);

            if (trades.Count == 0 && priceAlerts.Count == 0)
            {
                return;
            }

            IMarketDataService marketData = scope.ServiceProvider.GetRequiredService<IMarketDataService>();
            Dictionary<string, double?> prices = await LoadPricesAsync(marketData, trades.Select(t => t.Symbol).Concat(priceAlerts.Select(a => a.Symbol)), cancellationToken);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<Alert> raised = [];
            IJournalService journal = scope.ServiceProvider.GetRequiredService<IJournalService>();
            Dictionary<string, string> currencies = [];

            foreach (JournalTrade trade in trades)
            {
                if (prices.GetValueOrDefault(trade.Symbol) is not { } price || LevelChecker.Check(trade, price) is not { } level)
                {
                    continue;
                }

                if (level == TradeLevel.StopLoss)
                {
                    trade.StopLossAlerted = true;
                }
                else
                {
                    trade.TakeProfitAlerted = true;
                }

                if (!currencies.TryGetValue(trade.UserId, out string? currency))
                {
                    currency = (await journal.GetAccountAsync(trade.UserId, cancellationToken)).Currency;
                    currencies[trade.UserId] = currency;
                }

                raised.Add(AlertFactory.ForTradeLevel(trade, level, price, now, currency));
            }

            foreach (PriceAlert alert in priceAlerts)
            {
                if (prices.GetValueOrDefault(alert.Symbol) is { } price && LevelChecker.IsTriggered(alert, price))
                {
                    alert.TriggeredAt = now;
                    raised.Add(AlertFactory.ForPriceAlert(alert, price, now));
                }
            }

            if (raised.Count > 0)
            {
                await scope.ServiceProvider.GetRequiredService<IAlertService>().RaiseAsync(raised, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Price watch cycle failed");
        }
    }

    private async Task<Dictionary<string, double?>> LoadPricesAsync(IMarketDataService marketData, IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        Dictionary<string, double?> prices = [];

        foreach (string symbol in symbols.Distinct())
        {
            try
            {
                prices[symbol] = await marketData.GetLatestPriceAsync(symbol, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogDebug("No price for {Symbol}: {Message}", symbol, ex.Message);
            }
        }

        return prices;
    }
}
