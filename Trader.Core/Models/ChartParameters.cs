namespace Trader.Core.Models;

public static class ChartParameters
{
    public static readonly IReadOnlySet<string> AllowedIntervals = new HashSet<string> { "1m", "2m", "5m", "15m", "30m", "60m", "1d", "1wk", "1mo" };

    public static readonly IReadOnlySet<string> AllowedRanges = new HashSet<string> { "1d", "5d", "1mo", "3mo", "6mo", "1y", "2y", "5y", "10y", "max" };

    public static bool IsValid(string interval, string range) =>
        AllowedIntervals.Contains(interval) && AllowedRanges.Contains(range);
}

public sealed record LiveCandles(string Symbol, string Interval, IReadOnlyList<Candle> Candles);
