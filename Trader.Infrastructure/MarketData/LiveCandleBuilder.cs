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

    public static IReadOnlyList<Candle> Build(string interval, ChartSeries series)
    {
        if (series.Candles.Count == 0 || series.LastPrice is not { } price)
        {
            return series.Candles.TakeLast(2).ToList();
        }

        Candle last = series.Candles[^1];

        if (!IntradaySpans.TryGetValue(interval, out TimeSpan span) || series.LastPriceTime is not { } priceTime)
        {
            return [.. series.Candles.TakeLast(2).SkipLast(1), Extend(last, price, last.Volume)];
        }

        double reported = series.Candles.Sum(c => c.Volume);
        double extraVolume = series.DayVolume is { } day ? Math.Max(0, day - reported) : 0;
        TimeSpan sinceLast = priceTime - last.Time;

        if (sinceLast < span)
        {
            return [.. series.Candles.TakeLast(2).SkipLast(1), Extend(last, price, last.Volume + extraVolume)];
        }

        DateTimeOffset bucketStart = last.Time + (span * Math.Floor(sinceLast / span));
        Candle live = new(bucketStart, last.Close, Math.Max(last.Close, price), Math.Min(last.Close, price), price, extraVolume);

        return [last, live];
    }

    private static Candle Extend(Candle candle, double price, double volume) =>
        candle with { Close = price, High = Math.Max(candle.High, price), Low = Math.Min(candle.Low, price), Volume = volume };
}
