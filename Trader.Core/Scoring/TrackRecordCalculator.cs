using Trader.Core.Models;

namespace Trader.Core.Scoring;

public static class TrackRecordCalculator
{
    private const double AgreementFloor = 0.1;

    public static TrackRecordDto Calculate(IReadOnlyList<SuggestionDto> closed, int openCount)
    {
        List<SuggestionDto> ordered = closed
            .Where(s => s.RMultiple is not null && s.ClosedAt is not null)
            .OrderBy(s => s.ClosedAt)
            .ToList();

        if (ordered.Count == 0)
        {
            return new TrackRecordDto(0, openCount, 0, 0, 0, 0, null, [], [], []);
        }

        double totalR = ordered.Sum(s => s.RMultiple!.Value);
        int wins = ordered.Count(s => s.RMultiple > 0);
        double grossProfit = ordered.Where(s => s.RMultiple > 0).Sum(s => s.RMultiple!.Value);
        double grossLoss = Math.Abs(ordered.Where(s => s.RMultiple < 0).Sum(s => s.RMultiple!.Value));

        double running = 0;
        List<CurvePoint> curve = ordered
            .Select(s =>
            {
                running += s.RMultiple!.Value;
                return new CurvePoint(s.ClosedAt!.Value, running);
            })
            .ToList();

        List<AccuracyStat> bySignal = ordered
            .SelectMany(s => s.Signals
                .Where(signal => Math.Sign(signal.Value) == DirectionSign(s) && Math.Abs(signal.Value) >= AgreementFloor)
                .Select(signal => (Key: signal.Name, Label: signal.Label, Win: s.RMultiple > 0)))
            .GroupBy(x => x.Key)
            .Select(g => new AccuracyStat(g.Key, g.First().Label, g.Count(), g.Count(x => x.Win) / (double)g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        List<AccuracyStat> bySource = ordered
            .SelectMany(s => s.Articles.Select(a => a.Source).Distinct().Select(source => (Source: source, Win: s.RMultiple > 0)))
            .GroupBy(x => x.Source)
            .Select(g => new AccuracyStat(g.Key, g.Key, g.Count(), g.Count(x => x.Win) / (double)g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new TrackRecordDto(
            ordered.Count,
            openCount,
            wins,
            wins / (double)ordered.Count,
            totalR / ordered.Count,
            totalR,
            grossLoss > 0 ? grossProfit / grossLoss : null,
            curve,
            bySignal,
            bySource);
    }

    private static int DirectionSign(SuggestionDto suggestion) => suggestion.Direction == TradeDirection.Buy ? 1 : -1;
}
