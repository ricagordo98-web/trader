using Trader.Core.Models;

namespace Trader.Core.Charts;

public sealed record AutoLevel(string Group, string Label, double Price);

public sealed record ChartEventDto(DateTimeOffset Time, string Kind, string Label, string Detail, string? Url);

public static class AutoLevelGroups
{
    public const string PreviousDay = "previous-day";
    public const string OpeningRange = "opening-range";
    public const string Pivots = "pivots";
    public const string Swing = "swing";
}

public static class AutoLevelCalculator
{
    private const int SwingDays = 20;
    private static readonly TimeSpan OpeningRangeLength = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<AutoLevel> Compute(IReadOnlyList<Candle> daily, IReadOnlyList<Candle> intraday)
    {
        List<AutoLevel> levels = [];
        List<Candle> days = daily.Where(c => c.Close > 0).OrderBy(c => c.Time).ToList();
        List<Candle> session = LastSession(intraday);
        DateTime? today = session.Count > 0 ? session[0].Time.UtcDateTime.Date : days.Count > 0 ? days[^1].Time.UtcDateTime.Date : null;

        List<Candle> completed = today is { } date ? days.Where(c => c.Time.UtcDateTime.Date < date).ToList() : days;

        if (completed.Count > 0)
        {
            Candle previous = completed[^1];
            AddPreviousDay(levels, previous);
            AddPivots(levels, previous);
            AddSwing(levels, completed);
        }

        AddOpeningRange(levels, session);
        return levels;
    }

    private static void AddPreviousDay(List<AutoLevel> levels, Candle previous)
    {
        levels.Add(new AutoLevel(AutoLevelGroups.PreviousDay, "Máx. dia anterior", previous.High));
        levels.Add(new AutoLevel(AutoLevelGroups.PreviousDay, "Mín. dia anterior", previous.Low));
        levels.Add(new AutoLevel(AutoLevelGroups.PreviousDay, "Fecho dia anterior", previous.Close));
    }

    private static void AddPivots(List<AutoLevel> levels, Candle previous)
    {
        double pivot = (previous.High + previous.Low + previous.Close) / 3;
        double range = previous.High - previous.Low;

        levels.Add(new AutoLevel(AutoLevelGroups.Pivots, "Pivot", pivot));
        levels.Add(new AutoLevel(AutoLevelGroups.Pivots, "R1", 2 * pivot - previous.Low));
        levels.Add(new AutoLevel(AutoLevelGroups.Pivots, "S1", 2 * pivot - previous.High));
        levels.Add(new AutoLevel(AutoLevelGroups.Pivots, "R2", pivot + range));
        levels.Add(new AutoLevel(AutoLevelGroups.Pivots, "S2", pivot - range));
    }

    private static void AddSwing(List<AutoLevel> levels, List<Candle> completed)
    {
        List<Candle> recent = completed.TakeLast(SwingDays).ToList();

        if (recent.Count >= 5)
        {
            levels.Add(new AutoLevel(AutoLevelGroups.Swing, $"Resistência ({recent.Count} d)", recent.Max(c => c.High)));
            levels.Add(new AutoLevel(AutoLevelGroups.Swing, $"Suporte ({recent.Count} d)", recent.Min(c => c.Low)));
        }
    }

    private static void AddOpeningRange(List<AutoLevel> levels, List<Candle> session)
    {
        if (session.Count == 0 || session[^1].Time < session[0].Time + OpeningRangeLength)
        {
            return;
        }

        List<Candle> opening = session.Where(c => c.Time < session[0].Time + OpeningRangeLength).ToList();
        levels.Add(new AutoLevel(AutoLevelGroups.OpeningRange, "Máx. 30 min", opening.Max(c => c.High)));
        levels.Add(new AutoLevel(AutoLevelGroups.OpeningRange, "Mín. 30 min", opening.Min(c => c.Low)));
    }

    private static List<Candle> LastSession(IReadOnlyList<Candle> intraday)
    {
        List<Candle> ordered = intraday.Where(c => c.Close > 0).OrderBy(c => c.Time).ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        DateTime day = ordered[^1].Time.UtcDateTime.Date;
        return ordered.Where(c => c.Time.UtcDateTime.Date == day).ToList();
    }
}

public static class HeikinAshi
{
    public static IReadOnlyList<Candle> Convert(IReadOnlyList<Candle> candles)
    {
        List<Candle> result = new(candles.Count);
        Candle? previous = null;

        foreach (Candle candle in candles)
        {
            double close = (candle.Open + candle.High + candle.Low + candle.Close) / 4;
            double open = previous is null ? (candle.Open + candle.Close) / 2 : (previous.Open + previous.Close) / 2;
            Candle converted = candle with { Open = open, Close = close, High = Math.Max(candle.High, Math.Max(open, close)), Low = Math.Min(candle.Low, Math.Min(open, close)) };
            result.Add(converted);
            previous = converted;
        }

        return result;
    }
}
