using FluentAssertions;
using Trader.Infrastructure.MarketData;

namespace Trader.Tests;

public class YahooChartParserTests
{
    private const string Json = """
        {"chart":{"result":[{"meta":{"regularMarketPrice":101.5},
        "timestamp":[1700000000,1700086400,1700172800],
        "indicators":{"quote":[{
          "open":[100,null,102],"high":[103,null,104],"low":[99,null,101],
          "close":[101,null,103],"volume":[1000,null,3000]}]}}],"error":null}}
        """;

    [Fact]
    public void Parse_ValidPayload_ReturnsCandlesAndLastPrice()
    {
        var series = YahooChartParser.Parse(Json);

        series.LastPrice.Should().Be(101.5);
        series.Candles.Should().HaveCount(2);
        series.Candles[0].Close.Should().Be(101);
        series.Candles[1].Volume.Should().Be(3000);
    }

    [Fact]
    public void Parse_NullCandleEntries_SkipsThem()
    {
        var series = YahooChartParser.Parse(Json);

        series.Candles.Select(c => c.Open).Should().Equal(100, 102);
    }

    [Fact]
    public void Parse_EmptyResult_ReturnsEmptySeries()
    {
        var series = YahooChartParser.Parse("""{"chart":{"result":null,"error":{"code":"Not Found"}}}""");

        series.LastPrice.Should().BeNull();
        series.Candles.Should().BeEmpty();
    }
}

public class QuoteChangeTests
{
    private static readonly Trader.Core.Models.Instrument Apple = new() { Symbol = "AAPL", Name = "Apple", Currency = "USD" };

    private static Trader.Core.Models.Candle Candle(int day, double close) =>
        new(DateTimeOffset.UnixEpoch.AddDays(day), close, close, close, close, 0);

    [Fact]
    public void BuildQuote_MarketNotYetOpenToday_ShowsLastSessionChange()
    {
        var series = new Trader.Core.Models.ChartSeries(104, [Candle(1, 100), Candle(2, 102), Candle(3, 104)]);

        var quote = Trader.Infrastructure.MarketData.MarketDataService.BuildQuote(Apple, series);

        quote.Change.Should().BeApproximately(2, 1e-9);
        quote.ChangePercent.Should().BeApproximately(2 / 102.0 * 100, 1e-9);
    }

    [Fact]
    public void BuildQuote_LivePriceAboveLastClose_UsesPreviousSessionAsReference()
    {
        var series = new Trader.Core.Models.ChartSeries(105, [Candle(1, 100), Candle(2, 102), Candle(3, 104.8)]);

        var quote = Trader.Infrastructure.MarketData.MarketDataService.BuildQuote(Apple, series);

        quote.Change.Should().BeApproximately(3, 1e-9);
    }

    [Fact]
    public void BuildQuote_SingleCandle_HasNoChange()
    {
        var quote = Trader.Infrastructure.MarketData.MarketDataService.BuildQuote(Apple, new Trader.Core.Models.ChartSeries(100, [Candle(1, 100)]));

        quote.Change.Should().BeNull();
        quote.Price.Should().Be(100);
    }
}
