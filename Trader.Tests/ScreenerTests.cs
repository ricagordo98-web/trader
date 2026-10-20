using FluentAssertions;
using Trader.Core.Market;
using Trader.Core.Models;

namespace Trader.Tests;

public class ScreenerTests
{
    private static InstrumentQuote Quote(string symbol, AssetClass assetClass, double? changePercent) =>
        new(symbol, $"{symbol} Inc", assetClass, "USD", 100, 1, changePercent, []);

    private static SuggestionDto Suggestion(string symbol, TradeDirection direction, double confidence) => new(
        1, symbol, symbol, direction, confidence, 100, 98, 105, 2.5, string.Empty,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(5), SuggestionStatus.Open, null, null, null, [], []);

    private static IReadOnlyList<ScreenerRow> Rows() => Screener.Build(
        [
            Quote("AAPL", AssetClass.Stock, 2.5),
            Quote("MSFT", AssetClass.Stock, -1.2),
            Quote("GC=F", AssetClass.Commodity, 0.3),
            Quote("EURUSD=X", AssetClass.Forex, null)
        ],
        [
            new SentimentSummary("AAPL", "Apple", 12, 0.45),
            new SentimentSummary("MSFT", "Microsoft", 8, -0.35),
            new SentimentSummary("GC=F", "Gold", 3, 0.05),
            new SentimentSummary("EURUSD=X", "EURUSD", 0, 0)
        ],
        [Suggestion("AAPL", TradeDirection.Buy, 0.7), Suggestion("AAPL", TradeDirection.Buy, 0.8)],
        new Dictionary<string, DateOnly> { ["MSFT"] = new DateOnly(2026, 1, 8) });

    [Fact]
    public void Build_JoinsSentimentSuggestionAndEarnings()
    {
        IReadOnlyList<ScreenerRow> rows = Rows();

        rows.Single(r => r.Quote.Symbol == "AAPL").Suggestion!.Confidence.Should().Be(0.8);
        rows.Single(r => r.Quote.Symbol == "MSFT").EarningsDate.Should().Be(new DateOnly(2026, 1, 8));
        rows.Single(r => r.Quote.Symbol == "EURUSD=X").Sentiment.Should().BeNull();
        rows.Single(r => r.Quote.Symbol == "GC=F").ArticleCount.Should().Be(3);
    }

    [Fact]
    public void Apply_ByAssetClass_KeepsOnlyThatClass() =>
        Screener.Apply(Rows(), new ScreenerFilter(AssetClass: AssetClass.Stock), ScreenerSort.Symbol, false)
            .Select(r => r.Quote.Symbol).Should().Equal("AAPL", "MSFT");

    [Fact]
    public void Apply_MinAbsChange_UsesAbsoluteValue() =>
        Screener.Apply(Rows(), new ScreenerFilter(MinAbsChangePercent: 1), ScreenerSort.Symbol, false)
            .Select(r => r.Quote.Symbol).Should().Equal("AAPL", "MSFT");

    [Theory]
    [InlineData(SentimentFilter.Positive, "AAPL")]
    [InlineData(SentimentFilter.Negative, "MSFT")]
    [InlineData(SentimentFilter.Neutral, "GC=F")]
    public void Apply_BySentimentBand(SentimentFilter sentiment, string expected) =>
        Screener.Apply(Rows(), new ScreenerFilter(Sentiment: sentiment), ScreenerSort.Symbol, false)
            .Select(r => r.Quote.Symbol).Should().Equal(expected);

    [Fact]
    public void Apply_OnlyWithSuggestionAndEarnings_FilterAsRequested()
    {
        Screener.Apply(Rows(), new ScreenerFilter(OnlyWithSuggestion: true), ScreenerSort.Symbol, false)
            .Select(r => r.Quote.Symbol).Should().Equal("AAPL");
        Screener.Apply(Rows(), new ScreenerFilter(OnlyEarningsSoon: true), ScreenerSort.Symbol, false)
            .Select(r => r.Quote.Symbol).Should().Equal("MSFT");
    }

    [Fact]
    public void Apply_Search_MatchesSymbolOrNameIgnoringCase() =>
        Screener.Apply(Rows(), new ScreenerFilter(Search: "micro"), ScreenerSort.Symbol, false).Should().BeEmpty();

    [Fact]
    public void Apply_SearchBySymbol_Matches() =>
        Screener.Apply(Rows(), new ScreenerFilter(Search: "msf"), ScreenerSort.Symbol, false).Select(r => r.Quote.Symbol).Should().Equal("MSFT");

    [Fact]
    public void Apply_SortByChangeDescending_PutsMissingValuesLast() =>
        Screener.Apply(Rows(), new ScreenerFilter(), ScreenerSort.Change, true)
            .Select(r => r.Quote.Symbol).Should().Equal("AAPL", "GC=F", "MSFT", "EURUSD=X");

    [Fact]
    public void Apply_SortByChangeAscending_PutsMissingValuesLast() =>
        Screener.Apply(Rows(), new ScreenerFilter(), ScreenerSort.Change, false)
            .Select(r => r.Quote.Symbol).Should().Equal("MSFT", "GC=F", "AAPL", "EURUSD=X");

    [Theory]
    [InlineData(3.0, true)]
    [InlineData(-3.0, false)]
    public void HeatColor_UsesGreenForGainsAndRedForLosses(double change, bool green) =>
        Screener.HeatColor(change, true).Should().Contain(green ? "34,197,94" : "239,68,68");

    [Fact]
    public void HeatColor_StrongerMovesAreMoreOpaque()
    {
        static double Alpha(string color) => double.Parse(color.Split(',')[^1].TrimEnd(')'), System.Globalization.CultureInfo.InvariantCulture);

        Alpha(Screener.HeatColor(3, true)).Should().BeGreaterThan(Alpha(Screener.HeatColor(0.5, true)));
        Screener.HeatColor(null, true).Should().Contain("148,163,184");
    }
}
