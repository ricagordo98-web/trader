using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed class PriceLookup
{
    private readonly DateTime[] _dates;
    private readonly double[] _closes;

    public PriceLookup(IReadOnlyList<Candle> candles)
    {
        List<Candle> ordered = candles.Where(c => c.Close > 0).OrderBy(c => c.Time).ToList();
        _dates = ordered.Select(c => c.Time.UtcDateTime.Date).ToArray();
        _closes = ordered.Select(c => c.Close).ToArray();
    }

    public double? CloseAtOrBefore(DateTimeOffset time)
    {
        int index = Array.BinarySearch(_dates, time.UtcDateTime.Date);

        if (index < 0)
        {
            index = ~index - 1;
        }

        return index >= 0 ? _closes[index] : null;
    }
}

public static class ContextMetrics
{
    public const int RelativeStrengthDays = 20;

    public static double? RelativeStrength(IReadOnlyList<Candle> assetDaily, int index, PriceLookup benchmark, int days = RelativeStrengthDays)
    {
        if (index < days || index >= assetDaily.Count)
        {
            return null;
        }

        double? benchmarkNow = benchmark.CloseAtOrBefore(assetDaily[index].Time);
        double? benchmarkThen = benchmark.CloseAtOrBefore(assetDaily[index - days].Time);
        double thenClose = assetDaily[index - days].Close;

        if (benchmarkNow is null || benchmarkThen is not > 0 || thenClose <= 0)
        {
            return null;
        }

        double asset = (assetDaily[index].Close / thenClose - 1) * 100;
        double market = (benchmarkNow.Value / benchmarkThen.Value - 1) * 100;

        return asset - market;
    }

    public static double DailyTrend(MarketSnapshot snapshot) =>
        (Math.Sign(snapshot.Price - snapshot.Ema20) + Math.Sign(snapshot.Ema20 - snapshot.Ema50)) / 2.0;

    public static double? Adx(IReadOnlyList<Candle> candles, int period = 14)
    {
        if (candles.Count < period * 2 + 1)
        {
            return null;
        }

        double smoothedTr = 0;
        double smoothedPlus = 0;
        double smoothedMinus = 0;
        List<double> dx = [];

        for (int i = 1; i < candles.Count; i++)
        {
            double upMove = candles[i].High - candles[i - 1].High;
            double downMove = candles[i - 1].Low - candles[i].Low;
            double plus = upMove > downMove && upMove > 0 ? upMove : 0;
            double minus = downMove > upMove && downMove > 0 ? downMove : 0;
            double trueRange = Math.Max(candles[i].High - candles[i].Low, Math.Max(Math.Abs(candles[i].High - candles[i - 1].Close), Math.Abs(candles[i].Low - candles[i - 1].Close)));

            if (i <= period)
            {
                smoothedTr += trueRange;
                smoothedPlus += plus;
                smoothedMinus += minus;
            }
            else
            {
                smoothedTr = smoothedTr - smoothedTr / period + trueRange;
                smoothedPlus = smoothedPlus - smoothedPlus / period + plus;
                smoothedMinus = smoothedMinus - smoothedMinus / period + minus;
            }

            if (i >= period && smoothedTr > 0)
            {
                double plusDi = 100 * smoothedPlus / smoothedTr;
                double minusDi = 100 * smoothedMinus / smoothedTr;
                double sum = plusDi + minusDi;
                dx.Add(sum > 0 ? 100 * Math.Abs(plusDi - minusDi) / sum : 0);
            }
        }

        if (dx.Count < period)
        {
            return null;
        }

        double adx = dx.Take(period).Average();

        foreach (double value in dx.Skip(period))
        {
            adx = (adx * (period - 1) + value) / period;
        }

        return adx;
    }
}
