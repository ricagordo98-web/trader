using FluentAssertions;
using Trader.Core.Backtesting;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Backtesting;
using Trader.Infrastructure.Suggestions;

namespace Trader.Tests;

public class ContextFilterTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 14, 30, 0, TimeSpan.Zero);

    private static MarketSnapshot Bullish() => new("TEST", "Test", 100, 2, 98, 95, 90, 30, 0.5, 0.1, 97.5, 110);

    private static SentimentSnapshot Sentiment(double value) => new(value, 10, null, []);

    private static SuggestionEngine Engine() => new(new ScoringOptions { SlAtrMultiple = 1.5, TpAtrMultiple = 2.5, MinRiskReward = 1.5 });

    [Fact]
    public void Assess_WhenAdxShowsASidewaysMarket_RecordsAShadow()
    {
        SuggestionDraft? draft = Engine().Assess(Bullish() with { Adx = 12 }, Sentiment(0.5));

        draft!.IsShadow.Should().BeTrue();
        draft.Reasons.Should().Contain(r => r.StartsWith("mercado lateral"));
    }

    [Fact]
    public void Assess_AgainstTheWeeklyTrend_RecordsAShadow()
    {
        SuggestionDraft? draft = Engine().Assess(Bullish() with { WeeklyTrend = -1 }, Sentiment(0.5));

        draft!.Reasons.Should().Contain("contra a tendência semanal");
    }

    [Fact]
    public void Assess_WithEarningsInsideTheWindowOrHighVix_RecordsAShadow()
    {
        SuggestionDraft? draft = Engine().Assess(Bullish() with { DaysToEarnings = 3, Vix = 35 }, Sentiment(0.5));

        draft!.Reasons.Should().Contain(r => r.StartsWith("resultados em 3"));
        draft.Reasons.Should().Contain(r => r.StartsWith("VIX elevado"));
    }

    [Fact]
    public void Assess_WithFavourableContext_StaysVisible()
    {
        SuggestionDraft? draft = Engine().Assess(Bullish() with { Adx = 30, WeeklyTrend = 1, DaysToEarnings = 20, Vix = 18, RelativeStrength = 4 }, Sentiment(0.5));

        draft!.IsShadow.Should().BeFalse();
        draft.Signals.Should().Contain(s => s.Name == "relative-strength" && s.Value > 0);
    }

    [Fact]
    public void Assess_WithoutContext_BehavesAsBefore()
    {
        SuggestionDraft? draft = Engine().Assess(Bullish(), Sentiment(0.5));

        draft!.IsShadow.Should().BeFalse();
        draft.Signals.Should().NotContain(s => s.Name == "relative-strength");
    }

    [Fact]
    public void Adx_IsHighInATrendAndLowInARange()
    {
        List<Candle> trend = Enumerable.Range(0, 60).Select(i => new Candle(Created.AddMinutes(5 * i), 100 + i, 101 + i, 99.5 + i, 100.8 + i, 1000)).ToList();
        List<Candle> range = Enumerable.Range(0, 60).Select(i => new Candle(Created.AddMinutes(5 * i), 100, 100.6 + (i % 2) * 0.1, 99.4 - (i % 2) * 0.1, 100 + (i % 2 == 0 ? 0.3 : -0.3), 1000)).ToList();

        ContextMetrics.Adx(trend)!.Value.Should().BeGreaterThan(40);
        ContextMetrics.Adx(range)!.Value.Should().BeLessThan(20);
        ContextMetrics.Adx(trend.Take(10).ToList()).Should().BeNull();
    }

    [Fact]
    public void RelativeStrength_IsTheAssetReturnMinusTheMarketReturn()
    {
        List<Candle> asset = Enumerable.Range(0, 30).Select(i => new Candle(Created.AddDays(i), 100, 100, 100, 100 + i, 1)).ToList();
        List<Candle> market = Enumerable.Range(0, 30).Select(i => new Candle(Created.AddDays(i), 100, 100, 100, 100 + i * 0.5, 1)).ToList();

        double? strength = ContextMetrics.RelativeStrength(asset, 25, new PriceLookup(market));

        strength.Should().BeApproximately(((125.0 / 105 - 1) - (112.5 / 102.5 - 1)) * 100, 1e-9);
        ContextMetrics.RelativeStrength(asset, 10, new PriceLookup(market)).Should().BeNull();
    }

    [Fact]
    public void Series_ComputesAdxAndAWeeklyTrendForALongUptrend()
    {
        List<Candle> candles = Enumerable.Range(0, 300).Select(i => new Candle(new DateTimeOffset(2025, 1, 1, 21, 0, 0, TimeSpan.Zero).AddDays(i), 100 + i * 0.5, 101 + i * 0.5, 99.5 + i * 0.5, 100.5 + i * 0.5, 1000)).ToList();

        IndicatorSeries series = IndicatorCalculator.ComputeSeries("X", "X", candles);

        series.Snapshots[^1]!.WeeklyTrend.Should().Be(1);
        series.Snapshots[^1]!.Adx.Should().BeGreaterThan(30);
        series.Snapshots[60]!.WeeklyTrend.Should().BeNull();
    }

    [Fact]
    public void IntradayAssess_FlagsSidewaysAndAgainstDailyTrend()
    {
        IntradayOptions options = new() { RecordShadow = true, ShadowMinConfidence = 0.3, MinAdx = 15, RequireDailyAlignment = true };
        IntradayEngine engine = new(options);
        IntradayContext context = IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now), now) with { Adx = 9, DailyTrend = -1 };

        SuggestionDraft? draft = engine.Assess(context, IntradayFixtures.NoNews());

        draft!.Reasons.Should().Contain(r => r.StartsWith("mercado lateral"));
        draft.Reasons.Should().Contain("contra a tendência diária");
    }

    [Fact]
    public void WeakCells_FlagsOnlyCellsWithProvenNegativeNetResult()
    {
        List<CellOutcome> outcomes = [];

        for (int day = 0; day < 25; day++)
        {
            DateOnly date = new DateOnly(2026, 9, 1).AddDays(day);
            outcomes.Add(new CellOutcome("Intraday:Crypto", date, -0.6 + (day % 3) * 0.05));
            outcomes.Add(new CellOutcome("Intraday:Stock", date, day % 2 == 0 ? 1.5 : -1));
        }

        outcomes.Add(new CellOutcome("Intraday:Forex", new DateOnly(2026, 9, 1), -1));

        IReadOnlySet<string> weak = WeakCells.Find(outcomes, 20);

        weak.Should().Contain("Intraday:Crypto");
        weak.Should().NotContain("Intraday:Stock");
        weak.Should().NotContain("Intraday:Forex");
    }

    [Fact]
    public void WeakCells_CountsSameDayOutcomesAsOneCase()
    {
        List<CellOutcome> outcomes = Enumerable.Range(0, 40).Select(_ => new CellOutcome("Swing:Stock", new DateOnly(2026, 9, 1), -1)).ToList();

        WeakCells.Find(outcomes, 20).Should().BeEmpty();
    }

    [Fact]
    public void Reliability_CountsIndependentBetsInsteadOfRawSuggestions()
    {
        SuggestionDto Make(int index, double r) =>
            new(index, "A", "A", TradeDirection.Buy, 0.7, 100, 98, 104, 2, "x", Created.AddMinutes(index), Created.AddDays(1), r > 0 ? SuggestionStatus.HitTakeProfit : SuggestionStatus.HitStopLoss, Created.AddHours(2), 100, r, [], [], Class: AssetClass.Stock);

        List<SuggestionDto> items = Enumerable.Range(0, 40).Select(i => Make(i, i % 2 == 0 ? 2 : -1)).ToList();

        ModelReliabilityReport report = ModelReliability.Calculate(items);

        report.IndependentCases.Should().Be(1);
        report.Verdict.Reasons.Should().Contain(r => r.Contains("apostas independentes"));
    }

    [Fact]
    public void MetaModel_WithTooFewSamples_TrainsNothing()
    {
        MetaModelFit fit = MetaModelTrainer.Train(SuggestionHorizon.Intraday, []);

        fit.Validated.Should().BeFalse();
        fit.Model.Should().BeNull();
        fit.Message.Should().Contain("200");
    }

    [Fact]
    public void MetaModel_LearnsWhichSignalPredictsAndValidatesOutOfSample()
    {
        Random random = new(11);
        List<SuggestionDto> items = [];

        for (int i = 0; i < 400; i++)
        {
            bool win = random.NextDouble() < 0.5;
            double informative = win ? 0.4 + random.NextDouble() * 0.5 : -0.5 + random.NextDouble() * 0.8;
            double noise = random.NextDouble() * 2 - 1;

            items.Add(new SuggestionDto(i, "A", "A", TradeDirection.Buy, 0.7, 100, 98, 104, 2, "x", Created.AddMinutes(i), Created.AddDays(1), win ? SuggestionStatus.HitTakeProfit : SuggestionStatus.HitStopLoss, Created.AddHours(2), 100, win ? 2 : -1,
                [new SignalResult("vwap", "VWAP", informative, 1, "x"), new SignalResult("gap", "Gap", noise, 1, "x")], [], SuggestionHorizon.Intraday));
        }

        MetaModelFit fit = MetaModelTrainer.Train(SuggestionHorizon.Intraday, items);

        fit.Validated.Should().BeTrue();
        fit.Model.Should().NotBeNull();
        fit.Features.First().Signal.Should().Be("vwap");

        SignalResult[] strong = [new SignalResult("vwap", "VWAP", 0.9, 1, "x"), new SignalResult("gap", "Gap", 0, 1, "x")];
        SignalResult[] weak = [new SignalResult("vwap", "VWAP", -0.5, 1, "x"), new SignalResult("gap", "Gap", 0, 1, "x")];

        fit.Model!.Predict(strong, TradeDirection.Buy).Should().BeGreaterThan(fit.Model.Predict(weak, TradeDirection.Buy));
        fit.Model.Predict(strong, TradeDirection.Sell).Should().BeLessThan(fit.Model.Predict(strong, TradeDirection.Buy));
    }

    private static List<Candle> SyntheticCandles(int seed, int days)
    {
        Random random = new(seed);
        double price = 100;
        DateTimeOffset start = new(2020, 1, 1, 14, 30, 0, TimeSpan.Zero);
        List<Candle> candles = [];

        for (int day = 0; day < days; day++)
        {
            double open = price;
            double close = Math.Max(5, open + Math.Sin(day / 40.0) * 0.6 + (random.NextDouble() - 0.5) * 3);
            candles.Add(new Candle(start.AddDays(day), open, Math.Max(open, close) + random.NextDouble() * 1.2, Math.Min(open, close) - random.NextDouble() * 1.2, close, 1000));
            price = close;
        }

        return candles;
    }

    [Fact]
    public void Backtest_WithMarketContext_BlocksTradesInHighVixAndNotesIt()
    {
        List<SymbolHistory> histories = Enumerable.Range(1, 6)
            .Select(i => new SymbolHistory($"S{i}", $"Synthetic {i}", AssetClass.Stock, SyntheticCandles(i, 900)))
            .ToList();
        List<Candle> calmVix = histories[0].Candles.Select(c => c with { Close = 15 }).ToList();
        List<Candle> panicVix = histories[0].Candles.Select(c => c with { Close = 45 }).ToList();
        BacktestRequest request = new() { UseMarketContext = true };
        ScoringOptions scoring = new() { MinAdx = 0, RequireWeeklyAlignment = false };

        BacktestResult calm = BacktestRunner.Run(histories, scoring, request, Created, null, new BacktestMarketContext(new Dictionary<string, IReadOnlyList<Candle>>(), calmVix));
        BacktestResult panic = BacktestRunner.Run(histories, scoring, request, Created, null, new BacktestMarketContext(new Dictionary<string, IReadOnlyList<Candle>>(), panicVix));

        calm.UsedMarketContext.Should().BeTrue();
        calm.Notes.Should().Contain(n => n.Contains("filtros de contexto"));
        calm.Strategy.Trades.Should().BeGreaterThan(0);
        panic.Strategy.Trades.Should().Be(0);
    }
}
