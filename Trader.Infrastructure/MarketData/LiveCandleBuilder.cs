using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.MarketData;

public static class LiveCandleBuilder
{
    private static readonly Dictionary<string, TimeSpan> IntradaySpans = new()
    {
        ["1m"] = TimeSpan.FromMinutes(1),
        ["2m"] = TimeSpan.FromMinutes(2),
        ["5m"] = TimeSpan.FromMinutes(5),
        ["15m"] = TimeSpan.FromMinutes(15),
        ["30m"] = TimeSpan.FromMinutes(30),
        ["60m"] = TimeSpan.FromMinutes(60)
    };

    public static IReadOnlyList<Candle> Build(string interval, ChartSeries series, IReadOnlyList<LiveTrade>? trades = null)
    {
        if (series.Candles.Count == 0)
        {
            return [];
        }

        LiveTrade? lastTrade = trades is { Count: > 0 } ? trades[^1] : null;
        bool tradeIsNewer = lastTrade is not null && (series.LastPriceTime is not { } known || lastTrade.Time > known);

        double? price = tradeIsNewer ? lastTrade!.Price : series.LastPrice;
        DateTimeOffset? priceTime = tradeIsNewer ? lastTrade!.Time : series.LastPriceTime;

        if (price is not { } current)
        {
            return series.Candles.TakeLast(2).ToList();
        }

        Candle last = series.Candles[^1];

        if (!IntradaySpans.TryGetValue(interval, out TimeSpan span) || priceTime is not { } time)
        {
            return [.. series.Candles.TakeLast(2).SkipLast(1), Extend(last, current, last.Volume)];
        }

        double reported = series.Candles.Sum(c => c.Volume);
        double extraVolume = series.DayVolume is { } day ? Math.Max(0, day - reported) : 0;
        TimeSpan sinceLast = time - last.Time;

        Candle live;
        Candle? previous;

        if (sinceLast < span)
        {
            live = Extend(last, current, last.Volume + extraVolume);
            previous = series.Candles.Count > 1 ? series.Candles[^2] : null;
        }
        else
        {
            DateTimeOffset bucketStart = last.Time + (span * Math.Floor(sinceLast / span));
            live = new Candle(bucketStart, last.Close, Math.Max(last.Close, current), Math.Min(last.Close, current), current, extraVolume);
            previous = last;
        }

        live = ApplyTrades(live, span, trades, openFromTrades: sinceLast >= span);

        return previous is null ? [live] : [previous, live];
    }

    private static Candle ApplyTrades(Candle candle, TimeSpan span, IReadOnlyList<LiveTrade>? trades, bool openFromTrades)
    {
        if (trades is null || trades.Count == 0)
        {
            return candle;
        }

        DateTimeOffset end = candle.Time + span;
        List<LiveTrade> inside = trades.Where(t => t.Time >= candle.Time && t.Time < end).ToList();

        if (inside.Count == 0)
        {
            return candle;
        }

        return candle with
        {
            Open = openFromTrades ? inside[0].Price : candle.Open,
            High = Math.Max(candle.High, inside.Max(t => t.Price)),
            Low = Math.Min(candle.Low, inside.Min(t => t.Price)),
            Close = inside[^1].Price,
            Volume = Math.Max(candle.Volume, inside.Sum(t => t.Volume))
        };
    }

    private static Candle Extend(Candle candle, double price, double volume) =>
        candle with { Close = price, High = Math.Max(candle.High, price), Low = Math.Min(candle.Low, price), Volume = volume };
}
