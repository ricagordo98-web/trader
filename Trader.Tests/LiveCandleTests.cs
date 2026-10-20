using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.MarketData;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

public class LiveCandleBuilderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 15, 0, 0, TimeSpan.Zero);

    private static Candle Bar(int minute, double open, double high, double low, double close, double volume) =>
        new(Start.AddMinutes(minute), open, high, low, close, volume);

    [Fact]
    public void Parse_MetaFields_AreRead()
    {
        ChartSeries series = YahooChartParser.Parse("""{"chart":{"result":[{"meta":{"regularMarketPrice":10.5,"regularMarketTime":1700000000,"regularMarketVolume":12345},"timestamp":[],"indicators":{"quote":[]}}]}}""");

        series.LastPrice.Should().Be(10.5);
        series.LastPriceTime.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000000));
        series.DayVolume.Should().Be(12345);
    }

    [Fact]
    public void Parse_TrailingPriceOnlyPoint_IsDropped()
    {
        ChartSeries series = YahooChartParser.Parse("""
            {"chart":{"result":[{"meta":{"regularMarketPrice":5},"timestamp":[1700000040,1700000100,1700000133],
            "indicators":{"quote":[{"open":[1,1,5],"high":[1,1,5],"low":[1,1,5],"close":[1,1,5],"volume":[10,20,0]}]}}]}}
            """);

        series.Candles.Select(c => c.Time.ToUnixTimeSeconds()).Should().Equal(1700000040, 1700000100);
    }

    [Fact]
    public void Build_PriceInNewMinute_CreatesLiveCandleWithDayVolumeDifference()
    {
        ChartSeries series = new(
            101,
            [Bar(0, 100, 100.5, 99.5, 100, 500), Bar(1, 100, 101, 99.8, 100.2, 300)],
            Start.AddMinutes(2).AddSeconds(33),
            DayVolume: 1000);

        IReadOnlyList<Candle> result = LiveCandleBuilder.Build("1m", series);

        result.Should().HaveCount(2);
        result[0].Should().Be(series.Candles[1]);
        result[1].Should().Be(new Candle(Start.AddMinutes(2), 100.2, 101, 100.2, 101, 200));
    }

    [Fact]
    public void Build_PriceStillInLastCandle_ExtendsItAndAddsExtraVolume()
    {
        ChartSeries series = new(
            102,
            [Bar(0, 100, 101, 99, 100.5, 500)],
            Start.AddSeconds(40),
            DayVolume: 560);

        IReadOnlyList<Candle> result = LiveCandleBuilder.Build("1m", series);

        result.Should().ContainSingle().Which.Should().Be(Bar(0, 100, 102, 99, 102, 560));
    }

    [Fact]
    public void Build_FiveMinuteInterval_SnapsNewCandleToBucketStart()
    {
        ChartSeries series = new(
            100.4,
            [Bar(0, 100, 100.5, 99.5, 100, 800)],
            Start.AddMinutes(7).AddSeconds(10),
            DayVolume: 900);

        IReadOnlyList<Candle> result = LiveCandleBuilder.Build("5m", series);

        result[^1].Time.Should().Be(Start.AddMinutes(5));
        result[^1].Volume.Should().Be(100);
    }

    [Fact]
    public void Build_DailyInterval_OnlyUpdatesPriceAndKeepsReportedVolume()
    {
        ChartSeries series = new(
            105,
            [Bar(0, 100, 101, 99, 100.5, 5000)],
            Start.AddHours(1),
            DayVolume: 9999);

        LiveCandleBuilder.Build("1d", series).Should().ContainSingle().Which.Should().Be(Bar(0, 100, 105, 99, 105, 5000));
    }

    [Fact]
    public void Build_NoDayVolume_LiveCandleHasZeroVolume()
    {
        ChartSeries series = new(100.4, [Bar(0, 100, 100.5, 99.5, 100, 800)], Start.AddMinutes(1).AddSeconds(5));

        LiveCandleBuilder.Build("1m", series)[^1].Volume.Should().Be(0);
    }
}

public class LiveCandleServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 15, 0, 0, TimeSpan.Zero);

    private static MarketDataService CreateService(IMarketDataProvider provider) => new(
        new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite("Data Source=:memory:").Options),
        provider,
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<MarketDataService>.Instance);

    [Fact]
    public async Task GetLiveCandlesAsync_RepeatedCallsWithinCacheWindow_HitProviderOnce()
    {
        IMarketDataProvider provider = Substitute.For<IMarketDataProvider>();
        provider.GetChartAsync("AAPL", "1m", "1d", Arg.Any<CancellationToken>())
            .Returns(new ChartSeries(100, [new Candle(Start, 100, 101, 99, 100, 1)], Start.AddSeconds(5), 1));
        MarketDataService service = CreateService(provider);

        await service.GetLiveCandlesAsync("AAPL", "1m", "1d", CancellationToken.None);
        await service.GetLiveCandlesAsync("AAPL", "1m", "1d", CancellationToken.None);

        await provider.Received(1).GetChartAsync("AAPL", "1m", "1d", Arg.Any<CancellationToken>());
    }
}
