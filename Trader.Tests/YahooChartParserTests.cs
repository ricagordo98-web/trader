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
