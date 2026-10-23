using FluentAssertions;
using Trader.Core.Journal;
using Trader.Core.Models;

namespace Trader.Tests;

public class JournalInsightsTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 21, 9, 30, 0, TimeSpan.Zero);

    private static JournalTradeDto Trade(DateTimeOffset entry, double pnl, string symbol = "AAA", string setup = "", string emotion = "") =>
        new(1, symbol, symbol, TradeDirection.Buy, entry, 100, 1, 98, 104, 1, 0, "T212", string.Empty, null, TradeStatus.Closed, entry.AddMinutes(30), 101, 2, pnl, pnl / 2, null, null, setup, string.Empty, emotion);

    [Fact]
    public void Analyze_BuildsEquityAndDrawdownCurves()
    {
        JournalInsights insights = JournalAnalyzer.Analyze([Trade(Day, 50), Trade(Day.AddHours(1), -30), Trade(Day.AddHours(2), -10), Trade(Day.AddHours(3), 20)], TimeZoneInfo.Utc);

        insights.Equity.Select(p => p.Value).Should().Equal(50, 20, 10, 30);
        insights.Drawdown.Select(p => p.Value).Should().Equal(0, -30, -40, -20);
        insights.MaxDrawdown.Should().Be(40);
    }

    [Fact]
    public void Analyze_GroupsByHourSetupAndEmotion_AndPicksBestAndWorstHour()
    {
        List<JournalTradeDto> trades = [];
        trades.AddRange(Enumerable.Range(0, 3).Select(i => Trade(Day.AddDays(i), 40, setup: "breakout", emotion: "calmo")));
        trades.AddRange(Enumerable.Range(0, 3).Select(i => Trade(Day.AddDays(i).AddHours(5), -25, setup: "reversão", emotion: "ansioso")));

        JournalInsights insights = JournalAnalyzer.Analyze(trades, TimeZoneInfo.Utc);

        insights.BestHour.Should().Be("09h");
        insights.WorstHour.Should().Be("14h");
        insights.BySetup.Select(g => g.Name).Should().Equal("breakout", "reversão");
        insights.ByEmotion.First(g => g.Name == "ansioso").NetPnl.Should().Be(-75);
        insights.ByHour.Single(g => g.Name == "09h").WinRate.Should().Be(1);
    }

    [Fact]
    public void Analyze_IgnoresHoursWithTooFewTradesForVerdict()
    {
        JournalInsights insights = JournalAnalyzer.Analyze([Trade(Day, 100), Trade(Day.AddDays(1), 100)], TimeZoneInfo.Utc);

        insights.BestHour.Should().BeNull();
        insights.WorstHour.Should().BeNull();
    }

    [Fact]
    public void DailyRiskGuard_FlagsLossAndTradeCountLimits()
    {
        AccountProfile account = new(10_000, 1, "EUR", 2, 3);
        DateTimeOffset now = Day.AddHours(8);

        DailyRiskStatus ok = DailyRiskGuard.Evaluate(account, [Trade(Day, -50)], now, TimeZoneInfo.Utc);
        ok.Message.Should().BeNull();

        DailyRiskStatus loss = DailyRiskGuard.Evaluate(account, [Trade(Day, -120), Trade(Day.AddHours(1), -100)], now, TimeZoneInfo.Utc);
        loss.LossLimitReached.Should().BeTrue();
        loss.Message.Should().Contain("limite de perda");

        DailyRiskStatus count = DailyRiskGuard.Evaluate(account, [Trade(Day, 10), Trade(Day.AddHours(1), 10), Trade(Day.AddHours(2), 10)], now, TimeZoneInfo.Utc);
        count.TradesLimitReached.Should().BeTrue();
    }

    [Fact]
    public void DailyRiskGuard_WithLimitsOff_NeverWarns()
    {
        DailyRiskStatus status = DailyRiskGuard.Evaluate(new AccountProfile(10_000, 1, "EUR"), [Trade(Day, -9_000)], Day.AddHours(8), TimeZoneInfo.Utc);

        status.Message.Should().BeNull();
    }

    [Fact]
    public void ValidateDetails_RejectsBadImagesAndLongFields()
    {
        JournalValidator.ValidateDetails(new TradeDetailsRequest("a", "b", "c", null, "data:image/png;base64,AAAA")).Should().BeEmpty();
        JournalValidator.ValidateDetails(new TradeDetailsRequest(null, null, null, null, "http://evil")).Should().NotBeEmpty();
        JournalValidator.ValidateDetails(new TradeDetailsRequest(new string('x', 41), null, null, null, null)).Should().NotBeEmpty();
    }
}
