using FluentAssertions;
using Trader.Core.Backtesting;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Tests;

public class ModelImprovementsTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 14, 30, 0, TimeSpan.Zero);

    private static Suggestion Buy() => new()
    {
        Symbol = "AAA",
        Direction = TradeDirection.Buy,
        Entry = 100,
        StopLoss = 98,
        TakeProfit = 106,
        CreatedAt = Created,
        ExpiresAt = Created.AddHours(3)
    };

    private static Candle At(int minutes, double high, double low, double close) =>
        new(Created.AddMinutes(minutes), close, high, low, close, 1000);

    [Fact]
    public void Resolve_RecordsTheExcursionsAroundTheEntry()
    {
        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), [At(5, 102, 99, 101), At(10, 103, 99.5, 102), At(15, 106.5, 101, 106)], Created.AddHours(1));

        result!.Status.Should().Be(SuggestionStatus.HitTakeProfit);
        result.MfeR.Should().BeApproximately(3.25, 1e-9);
        result.MaeR.Should().BeApproximately(0.5, 1e-9);
        result.FixedR.Should().BeApproximately(3, 1e-9);
    }

    [Fact]
    public void Resolve_SmartExit_TurnsAReversalIntoASmallWinInsteadOfAFullLoss()
    {
        ExitPolicy policy = new(UseSmartExit: false, PartialAtR: 1, PartialFraction: 0.5, BreakevenAtR: 1, LockR: 0, TrailAfterR: 0);
        IReadOnlyList<Candle> candles = [At(5, 102.5, 100.5, 102), At(10, 102, 97, 98)];

        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), candles, Created.AddHours(1), policy);

        result!.Status.Should().Be(SuggestionStatus.HitStopLoss);
        result.FixedR.Should().Be(-1);
        result.SmartR.Should().BeApproximately(0.5, 1e-9);
        result.RMultiple.Should().Be(-1);
    }

    [Fact]
    public void Resolve_WhenSmartExitIsOfficial_UsesItsResultAsTheR()
    {
        ExitPolicy policy = new(UseSmartExit: true, PartialAtR: 1, PartialFraction: 0.5, BreakevenAtR: 1, LockR: 0, TrailAfterR: 0);

        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), [At(5, 102.5, 100.5, 102), At(10, 102, 97, 98)], Created.AddHours(1), policy);

        result!.RMultiple.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Resolve_Trailing_LocksInProfitWhenThePriceKeepsRunningThenFalls()
    {
        ExitPolicy policy = new(UseSmartExit: false, PartialAtR: 1, PartialFraction: 0, BreakevenAtR: 1, LockR: 0, TrailAfterR: 1.5, TrailDistanceR: 1);
        IReadOnlyList<Candle> candles = [At(5, 104, 100.5, 103.8), At(10, 104.5, 102.2, 103), At(15, 103, 101.5, 102)];

        OutcomeResult? result = OutcomeResolver.Resolve(Buy(), candles, Created.AddHours(4), policy);

        result!.SmartR.Should().BeApproximately(1.25, 1e-9);
    }

    [Fact]
    public void CostR_ConvertsTheRoundTripCostIntoRMultiples()
    {
        SuggestionDto suggestion = new(1, "AAA", "AAA", TradeDirection.Buy, 0.7, 100, 99, 102, 2, "x", Created, Created.AddHours(3), SuggestionStatus.HitTakeProfit, Created.AddHours(1), 102, 2, [], [], SuggestionHorizon.Intraday);

        double cost = SuggestionCosts.CostR(suggestion, AssetClass.Stock, new CostModel { StockRoundTripPercent = 0.1 });

        cost.Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void Reliability_UsesNetROfCostsAndSplitsByClassAndVariant()
    {
        SuggestionDto Make(double r, double cost, AssetClass assetClass, bool shadow = false, string variant = "A") =>
            new(1, "A", "A", TradeDirection.Buy, 0.7, 100, 98, 104, 2, "x", Created, Created.AddDays(1), r > 0 ? SuggestionStatus.HitTakeProfit : SuggestionStatus.HitStopLoss, Created.AddHours(2), 100, r, [], [],
                IsShadow: shadow, ShadowReason: shadow ? "variante B (teste)" : string.Empty, CostR: cost, Variant: variant, Class: assetClass);

        List<SuggestionDto> items = [Make(0.2, 0.3, AssetClass.Crypto), Make(2, 0.1, AssetClass.Stock), Make(-1, 0.1, AssetClass.Stock), Make(2, 0.1, AssetClass.Stock, true, "B")];

        ModelReliabilityReport report = ModelReliability.CalculateWithShadow(items, TimeZoneInfo.Utc);

        report.Closed.Should().Be(3);
        report.AverageGrossR.Should().BeApproximately((0.2 + 2 - 1) / 3.0, 1e-9);
        report.AverageR.Should().BeApproximately((-0.1 + 1.9 - 1.1) / 3.0, 1e-9);
        report.ByAssetClass!.Select(g => g.Name).Should().Contain(["Cripto", "Ações"]);
        report.ByVariant!.Select(g => g.Name).Should().Contain(["Variante A (em uso)", "Variante B (teste)"]);
    }

    [Fact]
    public void Excursions_SuggestAStopFromTheHeatTheWinnersTook()
    {
        SuggestionDto Make(bool win, double mfe, double mae) =>
            new(1, "A", "A", TradeDirection.Buy, 0.7, 100, 98, 104, 2, "x", Created, Created.AddDays(1), win ? SuggestionStatus.HitTakeProfit : SuggestionStatus.HitStopLoss, Created.AddHours(2), 100, win ? 2 : -1, [], [],
                MfeR: mfe, MaeR: mae);

        List<SuggestionDto> items = [.. Enumerable.Range(0, 10).Select(_ => Make(true, 2.2, 0.3)), .. Enumerable.Range(0, 6).Select(_ => Make(false, 1.2, 1.0))];

        ExcursionAnalysis? analysis = ModelReliability.AnalyzeExcursions(items);

        analysis.Should().NotBeNull();
        analysis!.WinnersMaeP80.Should().BeApproximately(0.3, 1e-9);
        analysis.StoppedReachedHalfTargetShare.Should().Be(1);
        analysis.Hint.Should().Contain("stop mais curto");
    }

    [Fact]
    public void SmartExitComparison_FlagsWhenItWouldHaveHelped()
    {
        SuggestionDto Make(double fixedR, double smartR) =>
            new(1, "A", "A", TradeDirection.Buy, 0.7, 100, 98, 104, 2, "x", Created, Created.AddDays(1), SuggestionStatus.HitStopLoss, Created.AddHours(2), 100, fixedR, [], [], SmartR: smartR);

        SmartExitComparison? comparison = ModelReliability.CompareSmartExit(Enumerable.Range(0, 12).Select(_ => Make(-1, 0.2)).ToList());

        comparison!.DifferenceR.Should().BeApproximately(1.2, 1e-9);
        comparison.Hint.Should().Contain("vale a pena");
    }

    [Fact]
    public void WeightLearner_WithTooFewSamples_ProposesNothing()
    {
        WeightLearning learning = WeightLearner.Learn(SuggestionHorizon.Intraday, [], new Dictionary<string, double> { ["vwap"] = 1 });

        learning.Validated.Should().BeFalse();
        learning.Weights.Should().BeEmpty();
        learning.Message.Should().Contain("150");
    }

    [Fact]
    public void WeightLearner_RaisesTheWeightOfASignalThatPredictsAndValidatesOutOfSample()
    {
        Random random = new(7);
        List<SuggestionDto> items = [];

        for (int i = 0; i < 300; i++)
        {
            bool win = random.NextDouble() < 0.5;
            double informative = win ? 0.8 : -0.2 + random.NextDouble() * 0.6;
            double noise = random.NextDouble() * 2 - 1;

            items.Add(new SuggestionDto(i, "A", "A", TradeDirection.Buy, 0.7, 100, 98, 104, 2, "x", Created.AddMinutes(i), Created.AddDays(1), win ? SuggestionStatus.HitTakeProfit : SuggestionStatus.HitStopLoss, Created.AddHours(2), 100, win ? 2 : -1,
                [new SignalResult("vwap", "VWAP", informative, 1, "x"), new SignalResult("gap", "Gap", noise, 1, "x")], [], SuggestionHorizon.Intraday));
        }

        WeightLearning learning = WeightLearner.Learn(SuggestionHorizon.Intraday, items, new Dictionary<string, double> { ["vwap"] = 1, ["gap"] = 1 });

        learning.Weights.Single(w => w.Signal == "vwap").ProposedWeight.Should().BeGreaterThan(learning.Weights.Single(w => w.Signal == "gap").ProposedWeight);
        learning.Validated.Should().BeTrue();
        learning.TestAucProposed.Should().BeGreaterThan(learning.TestAucCurrent!.Value);
    }

    [Fact]
    public void SignalWeightMap_RoundTripsTheOptions()
    {
        IntradayOptions options = new();
        SignalWeightMap.Apply(options, new Dictionary<string, double> { ["vwap"] = 1.7, ["market"] = 0.2 });

        SignalWeightMap.Read(options)["vwap"].Should().Be(1.7);
        options.MarketWeight.Should().Be(0.2);

        ScoringOptions scoring = new();
        SignalWeightMap.Apply(scoring, new Dictionary<string, double> { ["trend"] = 2 });
        SignalWeightMap.Read(scoring)["trend"].Should().Be(2);
    }

    [Fact]
    public void Sentiment_CategoryWeightsChangeTheInfluenceOfEachHeadlineType()
    {
        DateTimeOffset now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        List<ArticleSentimentRow> rows =
        [
            new("Company posts record quarterly earnings and revenue", "u1", "Wire", SourceType.News, 0.8, now.AddHours(-1)),
            new("Analyst upgrades the stock with a new price target", "u2", "Blog", SourceType.News, -0.8, now.AddHours(-1))
        ];

        SentimentSnapshot neutral = SentimentAggregator.Build(rows, now, new ScoringOptions());
        SentimentSnapshot learned = SentimentAggregator.Build(rows, now, new ScoringOptions(), null, new Dictionary<string, double> { ["Earnings"] = 1.4, ["Analyst"] = 0.6 });

        neutral.WeightedSentiment.Should().BeApproximately(0, 1e-9);
        learned.WeightedSentiment.Should().BeGreaterThan(0.2);
    }

    [Fact]
    public void Assess_FlagsVixAndAbnormalVolatility()
    {
        IntradayOptions options = new() { RecordShadow = true, ShadowMinConfidence = 0.3, MaxVix = 30, MaxVolatilityRatio = 2.5 };
        IntradayEngine engine = new(options);
        IntradayContext context = IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now), now) with { Vix = 35, VolatilityRatio = 3.1 };

        SuggestionDraft? draft = engine.Assess(context, new SentimentSnapshot(0, 0, null, []));

        draft.Should().NotBeNull();
        draft!.Reasons.Should().Contain(r => r.StartsWith("VIX elevado"));
        draft.Reasons.Should().Contain(r => r.StartsWith("volatilidade anormal"));
    }
}
