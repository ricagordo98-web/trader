using System.Globalization;
using Trader.Core.Models;

namespace Trader.Core.Journal;

public sealed record SeriesPoint(DateTimeOffset Time, double Value);

public sealed record InsightGroup(string Name, int Count, int Wins, double NetPnl, double? AverageR)
{
    public double WinRate => Count == 0 ? 0 : (double)Wins / Count;
}

public sealed record JournalInsights(
    int ClosedCount,
    IReadOnlyList<InsightGroup> ByHour,
    IReadOnlyList<InsightGroup> ByWeekday,
    IReadOnlyList<InsightGroup> BySymbol,
    IReadOnlyList<InsightGroup> BySetup,
    IReadOnlyList<InsightGroup> ByEmotion,
    IReadOnlyList<InsightGroup> ByDirection,
    IReadOnlyList<SeriesPoint> Equity,
    IReadOnlyList<SeriesPoint> Drawdown,
    double MaxDrawdown,
    double AverageMinutesHeld,
    string? BestHour,
    string? WorstHour);

public static class JournalAnalyzer
{
    public const int MinTradesForHourVerdict = 3;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static JournalInsights Analyze(IReadOnlyList<JournalTradeDto> trades, TimeZoneInfo zone)
    {
        List<JournalTradeDto> closed = trades
            .Where(t => t.Status == TradeStatus.Closed && t.NetPnl is not null && t.ExitTime is not null)
            .OrderBy(t => t.ExitTime)
            .ToList();

        List<SeriesPoint> equity = [];
        List<SeriesPoint> drawdown = [];
        double running = 0;
        double peak = 0;
        double maxDrawdown = 0;

        foreach (JournalTradeDto trade in closed)
        {
            running += trade.NetPnl!.Value;
            peak = Math.Max(peak, running);
            double depth = peak - running;
            maxDrawdown = Math.Max(maxDrawdown, depth);
            equity.Add(new SeriesPoint(trade.ExitTime!.Value, running));
            drawdown.Add(new SeriesPoint(trade.ExitTime!.Value, -depth));
        }

        List<InsightGroup> byHour = Group(closed, t => $"{Local(t.EntryTime, zone).Hour:00}h", ordered: true);
        List<InsightGroup> eligible = byHour.Where(g => g.Count >= MinTradesForHourVerdict).ToList();

        return new JournalInsights(
            closed.Count,
            byHour,
            Group(closed, t => Weekday(Local(t.EntryTime, zone)), ordered: false).OrderBy(g => WeekdayOrder(g.Name)).ToList(),
            Group(closed, t => t.Symbol, ordered: false).OrderByDescending(g => g.NetPnl).ToList(),
            Group(closed.Where(t => t.Setup.Length > 0).ToList(), t => t.Setup, ordered: false).OrderByDescending(g => g.NetPnl).ToList(),
            Group(closed.Where(t => t.Emotion.Length > 0).ToList(), t => t.Emotion, ordered: false).OrderByDescending(g => g.NetPnl).ToList(),
            Group(closed, t => t.Direction == TradeDirection.Buy ? "Compra" : "Venda", ordered: false),
            equity,
            drawdown,
            maxDrawdown,
            closed.Count == 0 ? 0 : closed.Average(t => (t.ExitTime!.Value - t.EntryTime).TotalMinutes),
            eligible.Count > 0 ? eligible.MaxBy(g => g.NetPnl)?.Name : null,
            eligible.Count > 0 ? eligible.MinBy(g => g.NetPnl)?.Name : null);
    }

    private static List<InsightGroup> Group(List<JournalTradeDto> trades, Func<JournalTradeDto, string> key, bool ordered)
    {
        IEnumerable<InsightGroup> groups = trades
            .GroupBy(key)
            .Select(g =>
            {
                List<double> rs = g.Where(t => t.RMultiple is not null).Select(t => t.RMultiple!.Value).ToList();
                return new InsightGroup(g.Key, g.Count(), g.Count(t => t.NetPnl > 0), g.Sum(t => t.NetPnl!.Value), rs.Count > 0 ? rs.Average() : null);
            });

        return (ordered ? groups.OrderBy(g => g.Name, StringComparer.Ordinal) : groups.OrderByDescending(g => g.Count)).ToList();
    }

    private static DateTimeOffset Local(DateTimeOffset time, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(time, zone);

    private static string Weekday(DateTimeOffset local) => Pt.DateTimeFormat.GetDayName(local.DayOfWeek);

    private static int WeekdayOrder(string name)
    {
        int index = Array.FindIndex(Enum.GetValues<DayOfWeek>(), d => Pt.DateTimeFormat.GetDayName(d) == name);
        return index == 0 ? 7 : index;
    }
}

public sealed record DailyRiskStatus(
    int TradesToday,
    int MaxTradesPerDay,
    double RealizedToday,
    double LossLimitAmount,
    bool TradesLimitReached,
    bool LossLimitReached,
    string? Message);

public static class DailyRiskGuard
{
    public static DailyRiskStatus Evaluate(AccountProfile account, IReadOnlyList<JournalTradeDto> trades, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        int opened = trades.Count(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.EntryTime, zone).DateTime) == today);
        double realized = trades
            .Where(t => t.Status == TradeStatus.Closed && t.ExitTime is { } exit && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(exit, zone).DateTime) == today)
            .Sum(t => t.NetPnl ?? 0);

        double lossLimit = account.MaxDailyLossPercent > 0 ? account.Capital * account.MaxDailyLossPercent / 100 : 0;
        bool tradesReached = account.MaxTradesPerDay > 0 && opened >= account.MaxTradesPerDay;
        bool lossReached = lossLimit > 0 && realized <= -lossLimit;

        string? message = (tradesReached, lossReached) switch
        {
            (_, true) => $"Atingiste o limite de perda diária ({account.MaxDailyLossPercent.ToString("0.#", Pt)}% do capital). Pára de operar por hoje.",
            (true, _) => $"Já abriste {opened} trades hoje, o teu limite diário. Pára e revê o dia.",
            _ => null
        };

        return new DailyRiskStatus(opened, account.MaxTradesPerDay, realized, lossLimit, tradesReached, lossReached, message);
    }

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");
}
