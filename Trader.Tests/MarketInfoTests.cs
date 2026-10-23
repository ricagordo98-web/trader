using FluentAssertions;
using Trader.Core.Calendar;
using Trader.Core.Market;
using Trader.Infrastructure.MarketData;

namespace Trader.Tests;

public class HaltParserTests
{
    private const string Feed = """
        <rss version="2.0" xmlns:ndaq="http://www.nasdaqtrader.com/">
          <channel>
            <item>
              <title>ABCD</title>
              <ndaq:HaltDate>10/22/2026</ndaq:HaltDate>
              <ndaq:HaltTime>10:15:00</ndaq:HaltTime>
              <ndaq:IssueSymbol>abcd</ndaq:IssueSymbol>
              <ndaq:IssueName>Abcd Inc</ndaq:IssueName>
              <ndaq:Market>NASDAQ</ndaq:Market>
              <ndaq:ReasonCode>T1</ndaq:ReasonCode>
              <ndaq:ResumptionDate></ndaq:ResumptionDate>
              <ndaq:ResumptionQuoteTime></ndaq:ResumptionQuoteTime>
              <ndaq:ResumptionTradeTime></ndaq:ResumptionTradeTime>
            </item>
            <item>
              <ndaq:HaltDate>10/22/2026</ndaq:HaltDate>
              <ndaq:HaltTime>09:45:10</ndaq:HaltTime>
              <ndaq:IssueSymbol>WXYZ</ndaq:IssueSymbol>
              <ndaq:Market>NYSE</ndaq:Market>
              <ndaq:ReasonCode>LUDP</ndaq:ReasonCode>
              <ndaq:ResumptionDate>10/22/2026</ndaq:ResumptionDate>
              <ndaq:ResumptionTradeTime>09:50:10</ndaq:ResumptionTradeTime>
            </item>
            <item><title>no symbol</title></item>
          </channel>
        </rss>
        """;

    [Fact]
    public void Parse_ReadsActiveAndResumedHaltsInUtc()
    {
        IReadOnlyList<TradingHalt> halts = HaltParser.Parse(Feed);

        halts.Should().HaveCount(2);
        TradingHalt active = halts.Single(h => h.Symbol == "ABCD");
        active.IsActive.Should().BeTrue();
        active.HaltedAt.Should().Be(new DateTimeOffset(2026, 10, 22, 14, 15, 0, TimeSpan.Zero));
        active.Reason.Should().Be("Notícias pendentes");
        active.Name.Should().Be("Abcd Inc");

        TradingHalt resumed = halts.Single(h => h.Symbol == "WXYZ");
        resumed.IsActive.Should().BeFalse();
        resumed.ResumedAt.Should().Be(new DateTimeOffset(2026, 10, 22, 13, 50, 10, TimeSpan.Zero));
    }

    [Fact]
    public void Monitor_AlertsOnceForActiveWatchlistHaltsOnly()
    {
        HaltMonitor monitor = new();
        IReadOnlyList<TradingHalt> halts = HaltParser.Parse(Feed);
        HashSet<string> watchlist = new(StringComparer.OrdinalIgnoreCase) { "ABCD", "WXYZ" };

        monitor.Update(halts, watchlist).Select(h => h.Symbol).Should().Equal("ABCD");
        monitor.Update(halts, watchlist).Should().BeEmpty();
        monitor.Update(halts, new HashSet<string>()).Should().BeEmpty();
        monitor.Halts.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("T1", "Notícias pendentes")]
    [InlineData("luDp", "Pausa por limite de preço (LULD)")]
    [InlineData("ZZ9", "Código ZZ9")]
    [InlineData("", "Motivo não indicado")]
    public void Reasons_AreDescribedInPortuguese(string code, string expected) => HaltReasons.Describe(code).Should().Be(expected);
}

public class MarketSessionsTests
{
    private static SessionStatus Find(IReadOnlyList<SessionStatus> sessions, string region) => sessions.Single(s => s.Region == region);

    [Fact]
    public void All_WednesdayAfternoonUtc_UsAndEuropeAreOpenAsiaIsClosed()
    {
        IReadOnlyList<SessionStatus> sessions = MarketSessions.All(new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero));

        Find(sessions, "us").IsOpen.Should().BeTrue();
        Find(sessions, "us").NextChange.Should().Be(new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.Zero));
        Find(sessions, "eu").IsOpen.Should().BeTrue();
        Find(sessions, "asia").IsOpen.Should().BeFalse();
        Find(sessions, "forex").IsOpen.Should().BeTrue();
        Find(sessions, "crypto").IsOpen.Should().BeTrue();
    }

    [Fact]
    public void All_SaturdayEverythingButCryptoIsClosedAndOpensLater()
    {
        DateTimeOffset saturday = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        IReadOnlyList<SessionStatus> sessions = MarketSessions.All(saturday);

        sessions.Where(s => s.Region != "crypto").Should().OnlyContain(s => !s.IsOpen);
        Find(sessions, "us").NextChange.Should().Be(new DateTimeOffset(2026, 10, 26, 13, 30, 0, TimeSpan.Zero));
        Find(sessions, "forex").NextChange.Should().Be(new DateTimeOffset(2026, 10, 25, 22, 0, 0, TimeSpan.Zero));
        Find(sessions, "crypto").IsOpen.Should().BeTrue();
    }

    [Fact]
    public void All_BeforeTheUsOpen_OpensToday()
    {
        IReadOnlyList<SessionStatus> sessions = MarketSessions.All(new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero));

        Find(sessions, "us").IsOpen.Should().BeFalse();
        Find(sessions, "us").NextChange.Should().Be(new DateTimeOffset(2026, 10, 21, 13, 30, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0, "em instantes")]
    [InlineData(5, "em 5 min")]
    [InlineData(135, "em 2 h 15 min")]
    [InlineData(3000, "em 2 dias")]
    public void Remaining_FormatsTheCountdown(int minutes, string expected) =>
        MarketSessions.Remaining(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
}

public class CalendarTimesTests
{
    [Fact]
    public void TimeFor_MacroEvents_UseTheUsReleaseTimes()
    {
        CalendarTimes.TimeFor(new CalendarEvent(new DateOnly(2026, 10, 28), CalendarKind.Macro, null, "Decisão de juros da Fed (FOMC)", string.Empty))
            .Should().Be(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero));
        CalendarTimes.TimeFor(new CalendarEvent(new DateOnly(2026, 11, 12), CalendarKind.Macro, null, "Inflação (CPI, EUA)", string.Empty))
            .Should().Be(new DateTimeOffset(2026, 11, 12, 13, 30, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("antes da abertura", 11, 30)]
    [InlineData("depois do fecho · EPS esperado 2,35", 21, 0)]
    [InlineData("durante a sessão", 15, 0)]
    public void TimeFor_Earnings_UsesTheAnnouncementWindow(string detail, int hour, int minute) =>
        CalendarTimes.TimeFor(new CalendarEvent(new DateOnly(2026, 10, 22), CalendarKind.Earnings, "AAPL", "Resultados de AAPL", detail))
            .Should().Be(new DateTimeOffset(2026, 10, 22, hour, minute, 0, TimeSpan.Zero));

    [Fact]
    public void TimeFor_UnknownEvents_HaveNoTime()
    {
        CalendarTimes.TimeFor(new CalendarEvent(new DateOnly(2026, 10, 22), CalendarKind.Earnings, "AAPL", "Resultados de AAPL", string.Empty)).Should().BeNull();
        CalendarTimes.TimeFor(new CalendarEvent(new DateOnly(2026, 10, 22), CalendarKind.Macro, null, "Outro evento", string.Empty)).Should().BeNull();
    }

    [Fact]
    public void Upcoming_ReturnsOnlyTimedEventsInsideTheWindowInOrder()
    {
        DateTimeOffset now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        List<CalendarEvent> events =
        [
            new(new DateOnly(2026, 10, 28), CalendarKind.Macro, null, "Decisão de juros da Fed (FOMC)", string.Empty),
            new(new DateOnly(2026, 10, 28), CalendarKind.Earnings, "AAA", "Resultados de AAA", "depois do fecho"),
            new(new DateOnly(2026, 10, 30), CalendarKind.Macro, null, "Inflação (CPI, EUA)", string.Empty),
            new(new DateOnly(2026, 10, 28), CalendarKind.Macro, null, "Outro evento", string.Empty)
        ];

        IReadOnlyList<TimedEvent> upcoming = CalendarTimes.Upcoming(events, now, TimeSpan.FromHours(12));

        upcoming.Select(t => t.Event.Title).Should().Equal("Decisão de juros da Fed (FOMC)", "Resultados de AAA");
        CalendarTimes.NextMacro(events, now, TimeSpan.FromHours(12))!.Event.Title.Should().Contain("FOMC");
        CalendarTimes.NextMacro(events, now, TimeSpan.FromMinutes(30)).Should().BeNull();
    }
}
