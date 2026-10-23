namespace Trader.Core.Scoring;

public static class SourceWeightCalculator
{
    public const int MinimumAppearances = 15;
    private const double MinWeight = 0.6;
    private const double MaxWeight = 1.4;

    public static IReadOnlyDictionary<string, double> Calculate(IEnumerable<(IReadOnlyList<string> Sources, bool Win)> outcomes)
    {
        List<(IReadOnlyList<string> Sources, bool Win)> list = outcomes.ToList();

        if (list.Count < MinimumAppearances)
        {
            return new Dictionary<string, double>();
        }

        double overall = list.Count(o => o.Win) / (double)list.Count;

        return list
            .SelectMany(o => o.Sources.Distinct().Select(source => (Source: source, o.Win)))
            .GroupBy(x => x.Source)
            .Where(g => g.Count() >= MinimumAppearances)
            .ToDictionary(g => g.Key, g => Math.Clamp(1 + (g.Count(x => x.Win) / (double)g.Count() - overall), MinWeight, MaxWeight));
    }
}
