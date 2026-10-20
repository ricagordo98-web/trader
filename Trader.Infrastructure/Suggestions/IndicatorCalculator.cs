using Skender.Stock.Indicators;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Infrastructure.Suggestions;

public sealed record IndicatorSeries(IReadOnlyList<Candle> Candles, IReadOnlyList<MarketSnapshot?> Snapshots);

public static class IndicatorCalculator
{
    private const int MinCandles = 50;
    private const int SwingWindow = 20;
    private const int LongEmaIndex = 200;

    public static MarketSnapshot? Compute(string symbol, string name, IReadOnlyList<Candle> candles) =>
        ComputeSeries(symbol, name, candles).Snapshots.LastOrDefault();

    public static IndicatorSeries ComputeSeries(string symbol, string name, IReadOnlyList<Candle> candles)
    {
        List<Candle> daily = candles
            .OrderBy(c => c.Time)
            .GroupBy(c => c.Time.UtcDateTime.Date)
            .Select(g => g.Last())
            .ToList();

        if (daily.Count < MinCandles)
        {
            return new IndicatorSeries(daily, daily.Select(_ => (MarketSnapshot?)null).ToList());
        }

        List<Quote> quotes = daily
            .Select(c => new Quote
            {
                Date = c.Time.UtcDateTime.Date,
                Open = (decimal)c.Open,
                High = (decimal)c.High,
                Low = (decimal)c.Low,
                Close = (decimal)c.Close,
                Volume = (decimal)c.Volume
            })
            .ToList();

        IReadOnlyList<EmaResult> ema20 = quotes.GetEma(20).ToList();
        IReadOnlyList<EmaResult> ema50 = quotes.GetEma(50).ToList();
        IReadOnlyList<EmaResult> ema200 = quotes.GetEma(LongEmaIndex).ToList();
        IReadOnlyList<RsiResult> rsi = quotes.GetRsi(14).ToList();
        IReadOnlyList<MacdResult> macd = quotes.GetMacd(12, 26, 9).ToList();
        IReadOnlyList<BollingerBandsResult> bollinger = quotes.GetBollingerBands(20, 2).ToList();
        IReadOnlyList<AtrResult> atr = quotes.GetAtr(14).ToList();

        List<MarketSnapshot?> snapshots = new(quotes.Count);

        for (int i = 0; i < quotes.Count; i++)
        {
            snapshots.Add(i < MinCandles - 1
                ? null
                : BuildSnapshot(symbol, name, quotes, i, ema20[i].Ema, ema50[i].Ema, i >= LongEmaIndex ? ema200[i].Ema : null, rsi[i].Rsi, macd[i].Histogram, bollinger[i].PercentB, atr[i].Atr));
        }

        return new IndicatorSeries(daily, snapshots);
    }

    private static MarketSnapshot? BuildSnapshot(
        string symbol,
        string name,
        List<Quote> quotes,
        int index,
        double? ema20,
        double? ema50,
        double? ema200,
        double? rsi,
        double? histogram,
        double? percentB,
        double? atr)
    {
        if (ema20 is null || ema50 is null || rsi is null || histogram is null || percentB is null || atr is null)
        {
            return null;
        }

        int start = Math.Max(0, index - SwingWindow + 1);
        decimal low = decimal.MaxValue;
        decimal high = decimal.MinValue;

        for (int k = start; k <= index; k++)
        {
            low = Math.Min(low, quotes[k].Low);
            high = Math.Max(high, quotes[k].High);
        }

        return new MarketSnapshot(
            symbol,
            name,
            (double)quotes[index].Close,
            atr.Value,
            ema20.Value,
            ema50.Value,
            ema200,
            rsi.Value,
            histogram.Value,
            percentB.Value,
            (double)low,
            (double)high);
    }
}
