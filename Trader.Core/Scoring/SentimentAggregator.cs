using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed record ArticleSentimentRow(
    string Title,
    string Url,
    string Source,
    SourceType SourceType,
    double Sentiment,
    DateTimeOffset PublishedAt);

public static class SentimentAggregator
{
    private const int RecentHours = 48;
    private const int BaselineDays = 29;
    private const int MinBaselineDays = 3;

    public static SentimentSnapshot Build(IReadOnlyList<ArticleSentimentRow> rows, DateTimeOffset now, ScoringOptions options)
    {
        List<ArticleSentimentRow> recent = rows.Where(r => now - r.PublishedAt <= TimeSpan.FromHours(RecentHours)).ToList();

        List<(ArticleSentimentRow Row, double Weight)> weighted = recent
            .Select(r => (Row: r, Weight: Math.Pow(0.5, Math.Max(0, (now - r.PublishedAt).TotalHours) / options.SentimentHalfLifeHours) * Reliability(r.SourceType)))
            .ToList();

        double totalWeight = weighted.Sum(w => w.Weight);
        double average = totalWeight > 0 ? weighted.Sum(w => w.Weight * w.Row.Sentiment) / totalWeight : 0;

        List<EvidenceArticle> articles = weighted
            .Select(w => new EvidenceArticle(w.Row.Title, w.Row.Url, w.Row.Source, w.Row.Sentiment, w.Row.PublishedAt, w.Weight * Math.Abs(w.Row.Sentiment)))
            .ToList();

        return new SentimentSnapshot(average, recent.Count, BuzzZScore(rows, now), articles);
    }

    private static double? BuzzZScore(IReadOnlyList<ArticleSentimentRow> rows, DateTimeOffset now)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        int historyDays = Math.Min(BaselineDays, (int)(now - rows.Min(r => r.PublishedAt)).TotalDays - 1);

        if (historyDays < MinBaselineDays)
        {
            return null;
        }

        int last24h = rows.Count(r => now - r.PublishedAt <= TimeSpan.FromHours(24));

        double[] dailyCounts = Enumerable.Range(1, historyDays)
            .Select(day => (double)rows.Count(r =>
            {
                TimeSpan age = now - r.PublishedAt;
                return age > TimeSpan.FromHours(24 * day) && age <= TimeSpan.FromHours(24 * (day + 1));
            }))
            .ToArray();

        double mean = dailyCounts.Average();
        double std = Math.Sqrt(dailyCounts.Sum(c => Math.Pow(c - mean, 2)) / dailyCounts.Length);

        return (last24h - mean) / Math.Max(std, 1);
    }

    private static double Reliability(SourceType type) => type switch
    {
        SourceType.Social => 0.6,
        SourceType.Filing => 1.2,
        _ => 1.0
    };
}
