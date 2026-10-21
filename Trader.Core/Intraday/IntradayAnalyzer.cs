using Trader.Core.Models;

namespace Trader.Core.Intraday;

public static class IntradayAnalyzer
{
    private const int AtrPeriod = 14;
    private const int MinSessionCandles = 3;
    private const int MinCandlesForVolumeRatio = 6;
    private const int RecentMoveCandles = 6;
    private const int MaxHistoryDays = 4;
    private const int MinHistoryDays = 2;
    private static readonly TimeSpan OpeningRangeLength = TimeSpan.FromMinutes(30);

    public static IntradayContext? Analyze(
        string symbol,
        string name,
        AssetClass assetClass,
        IReadOnlyList<Candle> candles,
        DateTimeOffset now,
        IntradayOptions options)
    {
        List<List<Candle>> days = candles
            .Where(c => c.Close > 0)
            .OrderBy(c => c.Time)
            .GroupBy(c => c.Time.UtcDateTime.Date)
            .Select(g => g.ToList())
            .ToList();

        if (days.Count == 0)
        {
            return null;
        }

        List<Candle> session = days[^1];
        Candle last = session[^1];

        if (session.Count < MinSessionCandles || now - last.Time > TimeSpan.FromMinutes(options.StaleAfterMinutes))
        {
            return null;
        }

        double atr = Atr(days);

        if (atr <= 0)
        {
            return null;
        }

        bool hasVolume = session.Any(c => c.Volume > 0);
        bool usesSessionLevels = assetClass is AssetClass.Stock or AssetClass.Etf;
        List<Candle>? previous = days.Count >= 2 ? days[^2] : null;
        double? previousClose = previous?[^1].Close;
        DateTimeOffset sessionStart = session[0].Time;
        double price = last.Close;
        double dayOpen = session[0].Open;

        double? gap = usesSessionLevels && previousClose is > 0 ? (dayOpen - previousClose.Value) / previousClose.Value * 100 : null;
        (double? openingHigh, double? openingLow) = usesSessionLevels ? OpeningRange(session, sessionStart) : (null, null);

        return new IntradayContext(
            symbol,
            name,
            assetClass,
            price,
            atr,
            hasVolume ? Vwap(session) : null,
            hasVolume ? RelativeVolume(days) : null,
            gap,
            openingHigh,
            openingLow,
            previous?.Max(c => c.High),
            previous?.Min(c => c.Low),
            previousClose,
            dayOpen,
            session.Max(c => c.High),
            session.Min(c => c.Low),
            RecentMove(session, atr),
            sessionStart,
            last.Time,
            session);
    }

    private static double Atr(List<List<Candle>> days)
    {
        List<double> ranges = [];

        foreach (List<Candle> day in days.TakeLast(3))
        {
            for (int i = 0; i < day.Count; i++)
            {
                double range = day[i].High - day[i].Low;

                if (i > 0)
                {
                    range = Math.Max(range, Math.Max(Math.Abs(day[i].High - day[i - 1].Close), Math.Abs(day[i].Low - day[i - 1].Close)));
                }

                ranges.Add(range);
            }
        }

        return ranges.Count < AtrPeriod ? 0 : ranges.TakeLast(AtrPeriod).Average();
    }

    private static double? Vwap(List<Candle> session)
    {
        double volume = session.Where(c => c.Volume > 0).Sum(c => c.Volume);

        return volume > 0
            ? session.Where(c => c.Volume > 0).Sum(c => (c.High + c.Low + c.Close) / 3 * c.Volume) / volume
            : null;
    }

    private static double? RelativeVolume(List<List<Candle>> days)
    {
        List<Candle> session = days[^1];
        Candle? lastWithVolume = session.LastOrDefault(c => c.Volume > 0);

        if (lastWithVolume is null || session.Count < MinCandlesForVolumeRatio)
        {
            return null;
        }

        TimeSpan cutoff = lastWithVolume.Time.UtcDateTime.TimeOfDay;
        double today = session.Where(c => c.Time.UtcDateTime.TimeOfDay <= cutoff).Sum(c => c.Volume);

        List<double> history = days
            .Take(days.Count - 1)
            .TakeLast(MaxHistoryDays)
            .Select(d => d.Where(c => c.Time.UtcDateTime.TimeOfDay <= cutoff).Sum(c => c.Volume))
            .Where(v => v > 0)
            .ToList();

        return history.Count >= MinHistoryDays && today > 0 ? today / history.Average() : null;
    }

    private static (double? High, double? Low) OpeningRange(List<Candle> session, DateTimeOffset sessionStart)
    {
        if (session[^1].Time < sessionStart + OpeningRangeLength)
        {
            return (null, null);
        }

        List<Candle> opening = session.Where(c => c.Time < sessionStart + OpeningRangeLength).ToList();
        return (opening.Max(c => c.High), opening.Min(c => c.Low));
    }

    private static double RecentMove(List<Candle> session, double atr)
    {
        Candle reference = session[Math.Max(0, session.Count - 1 - RecentMoveCandles)];
        return (session[^1].Close - reference.Close) / atr;
    }
}
