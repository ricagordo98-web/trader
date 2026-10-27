using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed record CellOutcome(string Key, DateOnly Day, double NetR);

public static class WeakCells
{
    private const double Z = 1.96;

    public static string Key(SuggestionHorizon horizon, AssetClass assetClass) => $"{horizon}:{assetClass}";

    public static string HourKey(SuggestionHorizon horizon, AssetClass assetClass, int utcHour) => $"{horizon}:{assetClass}:h{utcHour:00}";

    public static IReadOnlySet<string> Find(IEnumerable<CellOutcome> outcomes, int minIndependentCases)
    {
        HashSet<string> weak = [];

        foreach (IGrouping<string, CellOutcome> cell in outcomes.GroupBy(o => o.Key))
        {
            double[] days = cell.GroupBy(o => o.Day).Select(g => g.Average(o => o.NetR)).ToArray();

            if (days.Length < minIndependentCases)
            {
                continue;
            }

            double mean = days.Average();
            double variance = days.Sum(d => (d - mean) * (d - mean)) / (days.Length - 1);

            if (mean + Z * Math.Sqrt(variance / days.Length) < 0)
            {
                weak.Add(cell.Key);
            }
        }

        return weak;
    }
}
