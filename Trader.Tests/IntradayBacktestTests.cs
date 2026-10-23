using FluentAssertions;
using Trader.Core.Backtesting;
using Trader.Core.Intraday;
using Trader.Core.Models;

namespace Trader.Tests;

public class IntradayBacktestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 22, 0, 0, TimeSpan.Zero);

    private static IntradayHistory RallyDays(bool up = true)
    {
        List<Candle> candles = [];

        for (int day = 0; day < 4; day++)
        {
            candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 15).AddDays(day), 100, 78, _ => 100, 1000));
        }

        double direction = up ? 1 : -1;
        candles.AddRange(IntradayFixtures.Day(
            new DateTime(2026, 10, 20),
            100 + direction * 2,
            78,
            i => 100 + direction * (2 + 0.04 * Math.Max(0, i - 8)),
            3000));

        return new IntradayHistory("TEST", "Test Inc", AssetClass.Stock, candles);
    }

    private static IntradayHistory Flat()
    {
        List<Candle> candles = [];

        for (int day = 0; day < 5; day++)
        {
            candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 14).AddDays(day), 100, 78, i => 100 + 0.01 * (i % 3), 1000));
        }

        return new IntradayHistory("FLAT", "Flat Inc", AssetClass.Stock, candles);
    }

    private static IntradayBacktestRequest Request() => new() { Days = 25 };

    [Fact]
    public void Run_SteadyRally_FindsLongTradesThatBeatTheMirroredDirection()
    {
        IntradayBacktestResult result = IntradayBacktestRunner.Run([RallyDays()], IntradayFixtures.Options, Request(), Now);

        result.Strategy.Trades.Should().BeGreaterThan(0);
        result.SessionsTested.Should().BeGreaterThan(0);
        result.Strategy.TotalNetR.Should().BeGreaterThan(result.Mirrored.TotalNetR);
        result.AverageMinutesHeld.Should().BeGreaterThan(0);
        result.ByHour.Should().NotBeEmpty();
        result.BySignal.Select(g => g.Name).Should().Contain("vwap");
        result.Curve.Should().NotBeEmpty();
    }

    [Fact]
    public void Run_SteadySelloff_FindsShortTrades()
    {
        IntradayBacktestResult result = IntradayBacktestRunner.Run([RallyDays(up: false)], IntradayFixtures.Options, Request(), Now);

        result.Strategy.Trades.Should().BeGreaterThan(0);
        result.Strategy.TotalNetR.Should().BeGreaterThan(result.Mirrored.TotalNetR);
    }

    [Fact]
    public void Run_FlatMarket_FindsNoTrades()
    {
        IntradayBacktestResult result = IntradayBacktestRunner.Run([Flat()], IntradayFixtures.Options, Request(), Now);

        result.Strategy.Trades.Should().Be(0);
        result.Verdict.Code.Should().Be("insufficient");
    }

    [Fact]
    public void Run_OnlyUsesSessionsInsideTheRequestedWindow()
    {
        IntradayBacktestRequest request = new() { Days = 3 };

        IntradayBacktestResult result = IntradayBacktestRunner.Run([RallyDays()], IntradayFixtures.Options, request, new DateTimeOffset(2026, 10, 30, 22, 0, 0, TimeSpan.Zero));

        result.SessionsTested.Should().Be(0);
        result.Strategy.Trades.Should().Be(0);
    }

    [Fact]
    public void Run_Costs_ReduceTheNetResultComparedToGross()
    {
        IntradayBacktestResult result = IntradayBacktestRunner.Run([RallyDays()], IntradayFixtures.Options, Request(), Now);

        result.Strategy.AverageNetR.Should().BeLessThan(result.Strategy.AverageGrossR);
    }

    [Theory]
    [InlineData(2, 10, 0.02, 3)]
    [InlineData(40, 10, 0.02, 3)]
    [InlineData(10, 0, 0.02, 3)]
    [InlineData(10, 100, 0.02, 3)]
    [InlineData(10, 10, 2, 3)]
    [InlineData(10, 10, 0.02, 0)]
    public void Validate_RejectsOutOfRangeValues(int days, int symbols, double slippage, int step)
    {
        IntradayBacktestRequest request = new() { Days = days, MaxSymbols = symbols, SlippagePercent = slippage, StepCandles = step };

        IntradayBacktestRunner.Validate(request).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_DefaultRequest_IsAccepted() => IntradayBacktestRunner.Validate(new IntradayBacktestRequest()).Should().BeEmpty();
}
