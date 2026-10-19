using FluentAssertions;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Suggestions;

namespace Trader.Tests;

public class SuggestionEngineTests
{
    private readonly SuggestionEngine _engine = new(new ScoringOptions());

    private static MarketSnapshot Bullish(double swingLow = 97.5, double swingHigh = 110) =>
        new("TEST", "Test", 100, 2, 98, 95, 90, 30, 0.5, 0.1, swingLow, swingHigh);

    private static MarketSnapshot Bearish(double swingLow = 90, double swingHigh = 102.5) =>
        new("TEST", "Test", 100, 2, 102, 105, 110, 70, -0.5, 0.9, swingLow, swingHigh);

    private static SentimentSnapshot Sentiment(double value, int count = 10) => new(value, count, null, []);

    [Fact]
    public void Evaluate_AlignedBullishSignals_ReturnsBuyWithValidLevels()
    {
        SuggestionDraft? draft = _engine.Evaluate(Bullish(), Sentiment(0.5));

        draft.Should().NotBeNull();
        draft!.Direction.Should().Be(TradeDirection.Buy);
        draft.Confidence.Should().BeGreaterThanOrEqualTo(0.6);
        draft.StopLoss.Should().BeApproximately(97.3, 1e-9);
        draft.TakeProfit.Should().BeApproximately(105, 1e-9);
        draft.RiskReward.Should().BeGreaterThanOrEqualTo(1.5);
        draft.Signals.Select(s => s.Name).Should().Contain(["sentiment", "trend", "momentum", "bollinger"]);
    }

    [Fact]
    public void Evaluate_AlignedBearishSignals_ReturnsSellWithMirroredLevels()
    {
        SuggestionDraft? draft = _engine.Evaluate(Bearish(), Sentiment(-0.5));

        draft.Should().NotBeNull();
        draft!.Direction.Should().Be(TradeDirection.Sell);
        draft.StopLoss.Should().BeApproximately(102.7, 1e-9);
        draft.TakeProfit.Should().BeApproximately(95, 1e-9);
        draft.Explanation.Should().StartWith("Venda sugerida");
    }

    [Fact]
    public void Evaluate_SentimentContradictsTechnicals_ReturnsNull()
    {
        SuggestionDraft? draft = _engine.Evaluate(Bullish(), Sentiment(-0.5));

        draft.Should().BeNull();
    }

    [Fact]
    public void Evaluate_NoSentimentButAgreeingTechnicals_StillReturnsSuggestion()
    {
        SuggestionDraft? draft = _engine.Evaluate(Bullish(), Sentiment(0, 0));

        draft.Should().NotBeNull();
        draft!.Signals.Select(s => s.Name).Should().NotContain("sentiment");
    }

    [Fact]
    public void Evaluate_WideProtectiveSwingBreaksRiskReward_ReturnsNull()
    {
        SuggestionDraft? draft = _engine.Evaluate(Bullish(swingLow: 96), Sentiment(0.5));

        draft.Should().BeNull();
    }

    [Fact]
    public void Evaluate_NearbyResistance_CapsTakeProfit()
    {
        SuggestionDraft? draft = _engine.Evaluate(Bullish(swingHigh: 104.5), Sentiment(0.5));

        draft.Should().NotBeNull();
        draft!.TakeProfit.Should().BeApproximately(104.5, 1e-9);
    }

    [Fact]
    public void Evaluate_ZeroAtr_ReturnsNull()
    {
        _engine.Evaluate(Bullish() with { Atr = 0 }, Sentiment(0.5)).Should().BeNull();
    }
}

public class OutcomeResolverTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static Suggestion Buy() => new()
    {
        Direction = TradeDirection.Buy,
        Entry = 100,
        StopLoss = 98,
        TakeProfit = 105,
        CreatedAt = Created,
        ExpiresAt = Created.AddDays(5)
    };

    private static Candle At(int hours, double high, double low, double close) =>
        new(Created.AddHours(hours), (high + low) / 2, high, low, close, 0);

    [Fact]
    public void Resolve_TargetTouched_ClosesAsWinWithRiskReward()
    {
        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), [At(1, 101, 99.5, 100.5), At(2, 105.5, 100, 105)], Created.AddHours(3));

        result!.Status.Should().Be(SuggestionStatus.HitTakeProfit);
        result.RMultiple.Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void Resolve_StopTouched_ClosesAsMinusOneR()
    {
        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), [At(1, 101, 97.5, 98)], Created.AddHours(2));

        result!.Status.Should().Be(SuggestionStatus.HitStopLoss);
        result.RMultiple.Should().Be(-1);
    }

    [Fact]
    public void Resolve_StopAndTargetInSameCandle_AssumesStopFirst()
    {
        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), [At(1, 106, 97, 100)], Created.AddHours(2));

        result!.Status.Should().Be(SuggestionStatus.HitStopLoss);
    }

    [Fact]
    public void Resolve_SellDirection_UsesInvertedLevels()
    {
        Suggestion sell = new()
        {
            Direction = TradeDirection.Sell,
            Entry = 100,
            StopLoss = 102,
            TakeProfit = 96,
            CreatedAt = Created,
            ExpiresAt = Created.AddDays(5)
        };

        OutcomeResult? result = OutcomeResolver.Resolve(sell, [At(1, 100.5, 95.5, 96)], Created.AddHours(2));

        result!.Status.Should().Be(SuggestionStatus.HitTakeProfit);
        result.RMultiple.Should().BeApproximately(2, 1e-9);
    }

    [Fact]
    public void Resolve_NothingTouchedBeforeExpiry_ClosesAtLastCloseWithPartialR()
    {
        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), [At(1, 101, 99, 101)], Created.AddDays(6));

        result!.Status.Should().Be(SuggestionStatus.Expired);
        result.RMultiple.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Resolve_StillWithinWindow_ReturnsNull()
    {
        OutcomeResolver.Resolve(Buy(), [At(1, 101, 99, 100)], Created.AddDays(1)).Should().BeNull();
    }

    [Fact]
    public void Resolve_CandlesBeforeCreation_AreIgnored()
    {
        OutcomeResolver.Resolve(Buy(), [At(-3, 120, 50, 100)], Created.AddHours(1)).Should().BeNull();
    }
}

public class TrackRecordCalculatorTests
{
    private static SuggestionDto Closed(double r, int day, double sentimentValue, string source) => new(
        day, "T", "Test", TradeDirection.Buy, 0.7, 100, 98, 105, 2.5, string.Empty,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, r > 0 ? SuggestionStatus.HitTakeProfit : SuggestionStatus.HitStopLoss,
        DateTimeOffset.UnixEpoch.AddDays(day), 100, r,
        [new SignalResult("sentiment", "Sentimento", sentimentValue, 1, string.Empty)],
        [new EvidenceArticle("t", "u", source, 0.5, DateTimeOffset.UnixEpoch, 1)]);

    [Fact]
    public void Calculate_MixedResults_ComputesWinRateProfitFactorAndCurve()
    {
        TrackRecordDto record = TrackRecordCalculator.Calculate(
            [Closed(2, 1, 0.8, "CNBC"), Closed(-1, 2, 0.8, "CNBC"), Closed(1.5, 3, 0.8, "FT")],
            openCount: 2);

        record.ClosedCount.Should().Be(3);
        record.OpenCount.Should().Be(2);
        record.WinRate.Should().BeApproximately(2 / 3.0, 1e-9);
        record.TotalR.Should().BeApproximately(2.5, 1e-9);
        record.ProfitFactor.Should().BeApproximately(3.5, 1e-9);
        record.Curve.Select(c => c.CumulativeR).Should().Equal(2, 1, 2.5);
        record.BySource.Single(s => s.Name == "CNBC").WinRate.Should().BeApproximately(0.5, 1e-9);
        record.BySignal.Single(s => s.Name == "sentiment").Count.Should().Be(3);
    }

    [Fact]
    public void Calculate_NoClosedSuggestions_ReturnsEmptyRecord()
    {
        TrackRecordDto record = TrackRecordCalculator.Calculate([], 4);

        record.ClosedCount.Should().Be(0);
        record.OpenCount.Should().Be(4);
        record.ProfitFactor.Should().BeNull();
    }
}

public class SentimentAggregatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static ArticleSentimentRow Row(double sentiment, double hoursAgo, SourceType type = SourceType.News) =>
        new("t", "https://x.com/" + hoursAgo, "src", type, sentiment, Now.AddHours(-hoursAgo));

    [Fact]
    public void Build_RecentArticlesWeighMoreThanOld_ShiftsAverage()
    {
        SentimentSnapshot snapshot = SentimentAggregator.Build([Row(1, 1), Row(-1, 24)], Now, new ScoringOptions());

        snapshot.WeightedSentiment.Should().BeGreaterThan(0.5);
        snapshot.ArticleCount.Should().Be(2);
    }

    [Fact]
    public void Build_ShortHistory_HasNoBuzzScore()
    {
        SentimentAggregator.Build([Row(0.5, 1), Row(0.5, 30)], Now, new ScoringOptions()).BuzzZScore.Should().BeNull();
    }

    [Fact]
    public void Build_SpikeAgainstQuietBaseline_ReturnsPositiveBuzz()
    {
        List<ArticleSentimentRow> rows = [.. Enumerable.Range(1, 10).Select(i => Row(0.4, i))];
        rows.AddRange(Enumerable.Range(2, 8).Select(day => Row(0.1, day * 24 + 3)));

        double? buzz = SentimentAggregator.Build(rows, Now, new ScoringOptions()).BuzzZScore;

        buzz.Should().BeGreaterThan(1);
    }
}

public class IndicatorCalculatorTests
{
    [Fact]
    public void Compute_TooFewCandles_ReturnsNull()
    {
        IndicatorCalculator.Compute("X", "X", Enumerable.Range(0, 10).Select(i => Candle(i, 100 + i)).ToList()).Should().BeNull();
    }

    [Fact]
    public void Compute_SteadyUptrend_ProducesBullishSnapshot()
    {
        List<Candle> candles = Enumerable.Range(0, 250).Select(i => Candle(i, 100 + i * 0.5 + (i % 3 == 0 ? 0.4 : -0.2))).ToList();

        MarketSnapshot? snapshot = IndicatorCalculator.Compute("X", "X", candles);

        snapshot.Should().NotBeNull();
        snapshot!.Price.Should().BeGreaterThan(snapshot.Ema20);
        snapshot.Ema20.Should().BeGreaterThan(snapshot.Ema50);
        snapshot.Ema50.Should().BeGreaterThan(snapshot.Ema200!.Value);
        snapshot.Atr.Should().BeGreaterThan(0);
        snapshot.SwingHigh.Should().BeGreaterThanOrEqualTo(snapshot.SwingLow);
    }

    private static Candle Candle(int day, double close) =>
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day), close - 0.3, close + 0.8, close - 0.8, close, 1000);
}
