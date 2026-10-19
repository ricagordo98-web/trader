using Skender.Stock.Indicators;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Infrastructure.Suggestions;

public static class IndicatorCalculator
{
    private const int MinCandles = 50;
    private const int SwingWindow = 20;

    public static MarketSnapshot? Compute(string symbol, string name, IReadOnlyList<Candle> candles)
    {
        if (candles.Count < MinCandles)
        {
            return null;
        }

        List<Quote> quotes = candles
            .OrderBy(c => c.Time)
            .GroupBy(c => c.Time.UtcDateTime.Date)
            .Select(g => g.Last())
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

        if (quotes.Count < MinCandles)
        {
            return null;
        }

        double? ema20 = quotes.GetEma(20).LastOrDefault()?.Ema;
        double? ema50 = quotes.GetEma(50).LastOrDefault()?.Ema;
        double? ema200 = quotes.Count > 200 ? quotes.GetEma(200).LastOrDefault()?.Ema : null;
        double? rsi = quotes.GetRsi(14).LastOrDefault()?.Rsi;
        double? histogram = quotes.GetMacd(12, 26, 9).LastOrDefault()?.Histogram;
        double? percentB = quotes.GetBollingerBands(20, 2).LastOrDefault()?.PercentB;
        double? atr = quotes.GetAtr(14).LastOrDefault()?.Atr;

        if (ema20 is null || ema50 is null || rsi is null || histogram is null || percentB is null || atr is null)
        {
            return null;
        }

        List<Quote> window = quotes.TakeLast(SwingWindow).ToList();

        return new MarketSnapshot(
            symbol,
            name,
            (double)quotes[^1].Close,
            atr.Value,
            ema20.Value,
            ema50.Value,
            ema200,
            rsi.Value,
            histogram.Value,
            percentB.Value,
            (double)window.Min(q => q.Low),
            (double)window.Max(q => q.High));
    }
}
