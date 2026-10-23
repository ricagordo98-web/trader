using FluentAssertions;
using Trader.Core.Explain;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Tests;

public class SentimentDeduplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static ArticleSentimentRow Row(string title, double minutesAgo, double sentiment = 0.6, string source = "Reuters") =>
        new(title, $"https://x/{Math.Abs(title.GetHashCode())}/{minutesAgo}", source, SourceType.News, sentiment, Now.AddMinutes(-minutesAgo));

    [Fact]
    public void Deduplicate_MergesNearIdenticalHeadlinesWithinHours()
    {
        List<ArticleSentimentRow> rows =
        [
            Row("Apple unveils new foldable iPhone Duo at event", 120),
            Row("Apple unveils new foldable iPhone Duo at event - Bloomberg", 110),
            Row("Apple unveils its new foldable iPhone Duo at event", 90),
            Row("Fed holds interest rates steady", 60)
        ];

        SentimentAggregator.Deduplicate(rows).Select(r => r.Title).Should().Equal("Apple unveils new foldable iPhone Duo at event", "Fed holds interest rates steady");
    }

    [Fact]
    public void Deduplicate_KeepsSimilarHeadlinesThatAreFarApartInTime()
    {
        List<ArticleSentimentRow> rows =
        [
            Row("Tesla recalls vehicles over software issue", 60 * 30),
            Row("Tesla recalls vehicles over software issue", 30)
        ];

        SentimentAggregator.Deduplicate(rows).Should().HaveCount(2);
    }

    [Fact]
    public void Build_SyndicatedStoryCountsOnce()
    {
        List<ArticleSentimentRow> rows = Enumerable.Range(0, 6).Select(i => Row("Company wins major contract with government", 30 + i)).ToList();

        SentimentSnapshot snapshot = SentimentAggregator.Build(rows, Now, new ScoringOptions());

        snapshot.ArticleCount.Should().Be(1);
    }

    [Fact]
    public void Build_SourceWeights_ShiftTheAverageTowardsTrustedSources()
    {
        List<ArticleSentimentRow> rows =
        [
            Row("Alpha stock rallies on strong guidance outlook", 30, 0.9, "Good Source"),
            Row("Beta slumps after regulator probe announced today", 30, -0.9, "Bad Source")
        ];

        double neutral = SentimentAggregator.Build(rows, Now, new ScoringOptions()).WeightedSentiment;
        double weighted = SentimentAggregator.Build(rows, Now, new ScoringOptions(), new Dictionary<string, double> { ["Good Source"] = 1.4, ["Bad Source"] = 0.6 }).WeightedSentiment;

        neutral.Should().BeApproximately(0, 1e-9);
        weighted.Should().BeGreaterThan(0.3);
    }
}

public class SourceWeightCalculatorTests
{
    private static List<(IReadOnlyList<string>, bool)> Outcomes(string source, int wins, int losses) =>
        [.. Enumerable.Repeat(((IReadOnlyList<string>)[source], true), wins), .. Enumerable.Repeat(((IReadOnlyList<string>)[source], false), losses)];

    [Fact]
    public void Calculate_RewardsSourcesAboveAverageAndPenalisesTheOthers()
    {
        List<(IReadOnlyList<string>, bool)> outcomes = [.. Outcomes("Good", 16, 4), .. Outcomes("Bad", 4, 16)];

        IReadOnlyDictionary<string, double> weights = SourceWeightCalculator.Calculate(outcomes);

        weights["Good"].Should().BeGreaterThan(1.2);
        weights["Bad"].Should().BeLessThan(0.8);
    }

    [Fact]
    public void Calculate_IgnoresSourcesWithTooFewAppearances()
    {
        List<(IReadOnlyList<string>, bool)> outcomes = [.. Outcomes("Common", 20, 20), .. Outcomes("Rare", 3, 0)];

        SourceWeightCalculator.Calculate(outcomes).Keys.Should().Equal("Common");
    }

    [Fact]
    public void Calculate_TooLittleHistory_ReturnsNoWeights() =>
        SourceWeightCalculator.Calculate(Outcomes("Few", 5, 5)).Should().BeEmpty();
}

public class PeerComparisonTests
{
    private static MoveAnalysis Analysis(double change, params double[] peers) =>
        EvalCases.Move("X", "X Inc", change, 3, MoveScope.CompanySpecific, peers: peers.Select((p, i) => new PeerMove($"P{i}", $"P{i}", p, 0.8)).ToList());

    [Fact]
    public void PeerComparison_OutperformingThePeers_SaysSo() =>
        ExplanationText.PeerComparison(Analysis(5, 1, 2)).Should().Contain("subiu mais").And.Contain("3,5");

    [Fact]
    public void PeerComparison_InLineWithPeers_SaysSo() =>
        ExplanationText.PeerComparison(Analysis(2, 1.9, 2.2)).Should().Contain("Em linha");

    [Fact]
    public void PeerComparison_TooFewPeersOrSectorMove_IsOmitted()
    {
        ExplanationText.PeerComparison(Analysis(5, 1)).Should().BeNull();
        ExplanationText.PeerComparison(Analysis(5, 1, 2) with { Scope = MoveScope.Sector }).Should().BeNull();
    }
}
