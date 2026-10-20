using Trader.Core.Models;

namespace Trader.Core.Journal;

public static class JournalStatistics
{
    public static JournalStats Calculate(IReadOnlyList<JournalTradeDto> trades)
    {
        int openCount = trades.Count(t => t.Status == TradeStatus.Open);

        List<JournalTradeDto> closed = trades
            .Where(t => t.Status == TradeStatus.Closed && t.NetPnl is not null && t.ExitTime is not null)
            .OrderBy(t => t.ExitTime)
            .ToList();

        if (closed.Count == 0)
        {
            return new JournalStats(0, openCount, 0, 0, 0, 0, null, 0, null, 0, 0, 0, [], 0, null, null, []);
        }

        double[] pnl = closed.Select(t => t.NetPnl!.Value).ToArray();
        double grossProfit = pnl.Where(p => p > 0).Sum();
        double grossLoss = Math.Abs(pnl.Where(p => p < 0).Sum());
        List<double> rs = closed.Where(t => t.RMultiple is not null).Select(t => t.RMultiple!.Value).ToList();

        double running = 0;
        double peak = 0;
        double maxDrawdown = 0;
        List<CurvePoint> curve = [];

        foreach (JournalTradeDto trade in closed)
        {
            running += trade.NetPnl!.Value;
            peak = Math.Max(peak, running);
            maxDrawdown = Math.Max(maxDrawdown, peak - running);
            curve.Add(new CurvePoint(trade.ExitTime!.Value, running));
        }

        List<LinkedComparison> comparisons = closed
            .Where(t => t.SuggestionOutcome is not null && t.SuggestionR is not null && t.RMultiple is not null)
            .Select(t => new LinkedComparison(t.Id, t.Symbol, t.Direction, t.SuggestionOutcome!.Value, t.SuggestionR!.Value, t.RMultiple!.Value, t.ExitTime!.Value))
            .ToList();

        int wins = pnl.Count(p => p > 0);

        return new JournalStats(
            closed.Count,
            openCount,
            wins,
            wins / (double)closed.Count,
            pnl.Sum(),
            pnl.Average(),
            rs.Count > 0 ? rs.Average() : null,
            rs.Sum(),
            grossLoss > 0 ? grossProfit / grossLoss : null,
            maxDrawdown,
            pnl.Max(),
            pnl.Min(),
            curve,
            comparisons.Count,
            comparisons.Count > 0 ? comparisons.Average(c => c.YourR) : null,
            comparisons.Count > 0 ? comparisons.Average(c => c.SuggestionR) : null,
            comparisons);
    }
}

public static class AccountProfileValidator
{
    public static IReadOnlyList<string> Validate(AccountProfile profile)
    {
        List<string> errors = [];

        if (profile.Capital <= 0 || profile.Capital > 1_000_000_000)
        {
            errors.Add("O capital tem de ser positivo.");
        }

        if (profile.RiskPercent is <= 0 or > 10)
        {
            errors.Add("O risco por operação tem de estar entre 0% e 10%.");
        }

        if (string.IsNullOrWhiteSpace(profile.Currency) || profile.Currency.Trim().Length is < 1 or > 8)
        {
            errors.Add("Indica a moeda da conta (ex.: EUR).");
        }

        return errors;
    }
}
