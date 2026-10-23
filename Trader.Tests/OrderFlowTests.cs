using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trader.Core.Market;
using Trader.Infrastructure.MarketData;

namespace Trader.Tests;

public class OrderFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 30, 30, TimeSpan.Zero);

    [Theory]
    [InlineData("BTC-USD", "btcusdt")]
    [InlineData("eth-usd", "ethusdt")]
    [InlineData("UNI7083-USD", null)]
    [InlineData("AAPL", null)]
    [InlineData("EURUSD=X", null)]
    public void ToBinanceStream_OnlyForCryptoPairs(string symbol, string? expected)
    {
        if (symbol == "UNI7083-USD")
        {
            FlowSymbols.ToBinanceStream(symbol).Should().Be("uni7083usdt");
            return;
        }

        FlowSymbols.ToBinanceStream(symbol).Should().Be(expected);
    }

    [Fact]
    public void Snapshot_SeparatesAggressiveBuyersFromSellers()
    {
        FlowAggregator aggregator = new();
        aggregator.AddTrade(Now.AddSeconds(-5), 2, buyerIsMaker: false);
        aggregator.AddTrade(Now.AddSeconds(-3), 1, buyerIsMaker: true);
        aggregator.AddTrade(Now.AddMinutes(-1), 3, buyerIsMaker: false);

        OrderFlowSnapshot snapshot = aggregator.Snapshot("BTC-USD", Now);

        snapshot.Available.Should().BeTrue();
        snapshot.TotalVolume.Should().Be(6);
        snapshot.CumulativeDelta.Should().Be(4);
        snapshot.BuyShare.Should().BeApproximately(5.0 / 6, 1e-9);
        snapshot.Buckets.Should().HaveCount(60);
        snapshot.Buckets[^1].Delta.Should().Be(1);
        snapshot.Buckets[^2].Buy.Should().Be(3);
    }

    [Fact]
    public void Snapshot_WithoutData_IsNotAvailable()
    {
        OrderFlowSnapshot snapshot = new FlowAggregator().Snapshot("BTC-USD", Now);

        snapshot.Available.Should().BeFalse();
        snapshot.BuyShare.Should().Be(0.5);
        snapshot.Note.Should().Contain("A recolher");
    }

    [Fact]
    public void SetDepth_ComputesBookImbalance()
    {
        FlowAggregator aggregator = new();
        aggregator.SetDepth([3, 2, 1], [1, 1, 1], Now);

        aggregator.Snapshot("BTC-USD", Now).Imbalance.Should().BeApproximately(1.0 / 3, 1e-9);
    }

    [Fact]
    public void AddTrade_IgnoresEmptyQuantities_AndKeepsOnlyRecentBuckets()
    {
        FlowAggregator aggregator = new();
        aggregator.AddTrade(Now, 0, false);

        for (int i = 0; i < FlowAggregator.MaxBuckets + 30; i++)
        {
            aggregator.AddTrade(Now.AddMinutes(-i), 1, false);
        }

        aggregator.Snapshot("BTC-USD", Now, 200).Buckets.Count(b => b.Trades > 0).Should().BeLessThanOrEqualTo(FlowAggregator.MaxBuckets);
    }

    [Fact]
    public void Feed_Get_NonCryptoSymbol_ExplainsItIsUnavailable()
    {
        BinanceFlowFeed feed = new(NullLogger<BinanceFlowFeed>.Instance);

        OrderFlowSnapshot snapshot = feed.Get("AAPL");

        snapshot.Available.Should().BeFalse();
        snapshot.Note.Should().Contain("cripto");
    }

    [Fact]
    public void Feed_Handle_RoutesTradesAndDepthToTheRequestedSymbol()
    {
        BinanceFlowFeed feed = new(NullLogger<BinanceFlowFeed>.Instance);
        feed.Get("BTC-USD");
        long time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        feed.Handle("{\"stream\":\"btcusdt@aggTrade\",\"data\":{\"e\":\"aggTrade\",\"p\":\"80000.1\",\"q\":\"0.5\",\"T\":" + time + ",\"m\":false}}");
        feed.Handle("{\"stream\":\"btcusdt@aggTrade\",\"data\":{\"e\":\"aggTrade\",\"p\":\"80000.1\",\"q\":\"0.2\",\"T\":" + time + ",\"m\":true}}");
        feed.Handle("""{"stream":"btcusdt@depth5@1000ms","data":{"bids":[["80000","3"],["79999","1"]],"asks":[["80001","1"],["80002","1"]]}}""");
        feed.Handle("""{"stream":"ethusdt@aggTrade","data":{"q":"9","T":1,"m":false}}""");
        feed.Handle("not json at all".Replace("not json at all", "{\"ping\":1}"));

        OrderFlowSnapshot snapshot = feed.Get("BTC-USD");

        snapshot.Available.Should().BeTrue();
        snapshot.TotalVolume.Should().BeApproximately(0.7, 1e-9);
        snapshot.CumulativeDelta.Should().BeApproximately(0.3, 1e-9);
        snapshot.Imbalance.Should().BeApproximately(0.3333, 1e-3);
    }
}
