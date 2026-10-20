using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.MarketData;

public sealed class MarketDataService(
    TraderDbContext dbContext,
    IMarketDataProvider provider,
    IMemoryCache cache,
    ILogger<MarketDataService> logger) : IMarketDataService
{
    private static readonly TimeSpan QuoteCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CandleCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TickCacheDuration = TimeSpan.FromSeconds(1);

    public async Task<IReadOnlyList<InstrumentQuote>> GetQuotesAsync(AssetClass? assetClass, CancellationToken cancellationToken)
    {
        IQueryable<Instrument> query = dbContext.Instruments.AsNoTracking();

        if (assetClass is not null)
        {
            query = query.Where(i => i.AssetClass == assetClass);
        }

        List<Instrument> instruments = await query.OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);

        using SemaphoreSlim throttle = new(6);
        Task<InstrumentQuote>[] tasks = instruments
            .Select(async instrument =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    return await GetQuoteAsync(instrument, cancellationToken);
                }
                finally
                {
                    throttle.Release();
                }
            })
            .ToArray();

        return await Task.WhenAll(tasks);
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, string range, CancellationToken cancellationToken)
    {
        string key = $"candles:{symbol}:{interval}:{range}";

        if (cache.TryGetValue(key, out IReadOnlyList<Candle>? cached) && cached is not null)
        {
            return cached;
        }

        ChartSeries series = await provider.GetChartAsync(symbol, interval, range, cancellationToken);
        cache.Set(key, series.Candles, CandleCacheDuration);
        return series.Candles;
    }

    public async Task<LiveTick?> GetLiveTickAsync(string symbol, CancellationToken cancellationToken)
    {
        string key = $"tick:{symbol}";

        if (cache.TryGetValue(key, out LiveTick? cached))
        {
            return cached;
        }

        ChartSeries series = await provider.GetChartAsync(symbol, "1m", "1d", cancellationToken);
        double? price = series.LastPrice ?? series.Candles.LastOrDefault()?.Close;
        LiveTick? tick = price is { } value
            ? new LiveTick(symbol, value, series.LastPriceTime ?? series.Candles.LastOrDefault()?.Time ?? DateTimeOffset.UtcNow)
            : null;

        cache.Set(key, tick, TickCacheDuration);
        return tick;
    }

    private async Task<InstrumentQuote> GetQuoteAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        string key = $"quote:{instrument.Symbol}";

        if (cache.TryGetValue(key, out InstrumentQuote? cached) && cached is not null)
        {
            return cached;
        }

        InstrumentQuote quote;

        try
        {
            ChartSeries series = await provider.GetChartAsync(instrument.Symbol, "1d", "1mo", cancellationToken);
            quote = BuildQuote(instrument, series);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to load quote for {Symbol}", instrument.Symbol);
            return new InstrumentQuote(instrument.Symbol, instrument.Name, instrument.AssetClass, instrument.Currency, null, null, null, []);
        }

        cache.Set(key, quote, QuoteCacheDuration);
        return quote;
    }

    internal static InstrumentQuote BuildQuote(Instrument instrument, ChartSeries series)
    {
        double[] closes = series.Candles.Select(c => c.Close).ToArray();
        double? price = series.LastPrice ?? (closes.Length > 0 ? closes[^1] : null);

        double? change = null;
        double? changePercent = null;

        if (price is not null && closes.Length >= 2)
        {
            double previousClose = closes[^2];

            if (previousClose != 0)
            {
                change = price.Value - previousClose;
                changePercent = change / previousClose * 100;
            }
        }

        return new InstrumentQuote(
            instrument.Symbol,
            instrument.Name,
            instrument.AssetClass,
            instrument.Currency,
            price,
            change,
            changePercent,
            closes);
    }
}
