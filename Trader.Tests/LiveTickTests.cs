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

public class LiveTickTests
{
    [Fact]
    public void Parse_MarketTime_IsReadFromMeta()
    {
        ChartSeries series = YahooChartParser.Parse("""{"chart":{"result":[{"meta":{"regularMarketPrice":10.5,"regularMarketTime":1700000000},"timestamp":[],"indicators":{"quote":[]}}]}}""");

        series.LastPrice.Should().Be(10.5);
        series.LastPriceTime.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000000));
    }

    private static MarketDataService CreateService(IMarketDataProvider provider) => new(
        new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite("Data Source=:memory:").Options),
        provider,
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<MarketDataService>.Instance);

    [Fact]
    public async Task GetLiveTickAsync_UsesLastPriceAndMarketTime()
    {
        IMarketDataProvider provider = Substitute.For<IMarketDataProvider>();
        DateTimeOffset time = new(2026, 1, 5, 15, 0, 3, TimeSpan.Zero);
        provider.GetChartAsync("AAPL", "1m", "1d", Arg.Any<CancellationToken>()).Returns(new ChartSeries(188.4, [], time));

        LiveTick? tick = await CreateService(provider).GetLiveTickAsync("AAPL", CancellationToken.None);

        tick.Should().Be(new LiveTick("AAPL", 188.4, time));
    }

    [Fact]
    public async Task GetLiveTickAsync_RepeatedCallsWithinCacheWindow_HitProviderOnce()
    {
        IMarketDataProvider provider = Substitute.For<IMarketDataProvider>();
        provider.GetChartAsync("AAPL", "1m", "1d", Arg.Any<CancellationToken>()).Returns(new ChartSeries(100, []));
        MarketDataService service = CreateService(provider);

        await service.GetLiveTickAsync("AAPL", CancellationToken.None);
        await service.GetLiveTickAsync("AAPL", CancellationToken.None);

        await provider.Received(1).GetChartAsync("AAPL", "1m", "1d", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLiveTickAsync_NoPriceAvailable_ReturnsNull()
    {
        IMarketDataProvider provider = Substitute.For<IMarketDataProvider>();
        provider.GetChartAsync("XXX", "1m", "1d", Arg.Any<CancellationToken>()).Returns(new ChartSeries(null, []));

        (await CreateService(provider).GetLiveTickAsync("XXX", CancellationToken.None)).Should().BeNull();
    }
}
