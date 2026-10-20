using FluentAssertions;
using Trader.Core.Models;
using Trader.Web.Hubs;

namespace Trader.Tests;

public class LiveStreamingTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Active_AfterAddAndRemove_TracksConnections()
    {
        LiveSubscriptions subscriptions = new();
        LiveSubscription aapl = new("AAPL", "1m", "1d");

        subscriptions.Add("c1", aapl);
        subscriptions.Add("c2", aapl);
        subscriptions.Remove("c1", aapl);
        subscriptions.Active().Should().ContainSingle().Which.Should().Be(aapl);

        subscriptions.Remove("c2", aapl);
        subscriptions.Active().Should().BeEmpty();
    }

    [Fact]
    public void RemoveConnection_DropsItFromEveryGroup()
    {
        LiveSubscriptions subscriptions = new();
        subscriptions.Add("c1", new LiveSubscription("AAPL", "1m", "1d"));
        subscriptions.Add("c1", new LiveSubscription("MSFT", "5m", "1d"));

        IReadOnlyList<string> removed = subscriptions.RemoveConnection("c1");

        removed.Should().HaveCount(2);
        subscriptions.Active().Should().BeEmpty();
    }

    [Fact]
    public void HasChanged_SameCandles_ReturnsFalse()
    {
        Candle[] candles = [new(Start, 1, 2, 0.5, 1.5, 10)];

        LiveCandlePublisher.HasChanged(candles, [new Candle(Start, 1, 2, 0.5, 1.5, 10)]).Should().BeFalse();
    }

    [Fact]
    public void HasChanged_NewCloseOrFirstSend_ReturnsTrue()
    {
        Candle[] candles = [new(Start, 1, 2, 0.5, 1.5, 10)];

        LiveCandlePublisher.HasChanged(candles, [new Candle(Start, 1, 2, 0.5, 1.6, 10)]).Should().BeTrue();
        LiveCandlePublisher.HasChanged(null, candles).Should().BeTrue();
    }

    [Theory]
    [InlineData("1m", "1d", true)]
    [InlineData("2m", "5d", true)]
    [InlineData("3m", "1d", false)]
    [InlineData("1m", "forever", false)]
    public void ChartParameters_IsValid(string interval, string range, bool expected) =>
        ChartParameters.IsValid(interval, range).Should().Be(expected);
}
