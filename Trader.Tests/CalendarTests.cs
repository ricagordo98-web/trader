using System.Net;
using FluentAssertions;
using Trader.Core.Alerts;
using Trader.Core.Calendar;
using Trader.Core.Journal;
using Trader.Core.Models;
using Trader.Infrastructure.Calendar;

namespace Trader.Tests;

public class CalendarTests
{
    private static readonly DateOnly Today = new(2026, 1, 5);

    [Fact]
    public void ParseEarnings_KeepsOnlyWatchlistSymbolsAndDescribesTiming()
    {
        const string json = """
            {"earningsCalendar":[
              {"symbol":"AAPL","date":"2026-01-06","hour":"amc","epsEstimate":2.345,"revenueEstimate":94500000000,"quarter":1,"year":2026},
              {"symbol":"ZZZZ","date":"2026-01-06","hour":"bmo"},
              {"symbol":"MSFT","date":"2026-01-07","hour":"bmo"}]}
            """;

        IReadOnlyList<CalendarEvent> events = CalendarService.ParseEarnings(json, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT" });

        events.Should().HaveCount(2);
        events[0].Should().Be(new CalendarEvent(new DateOnly(2026, 1, 6), CalendarKind.Earnings, "AAPL", "Resultados de AAPL", "depois do fecho · EPS esperado 2,35", 2.345, 94500000000, "T1 2026"));
        events[1].Detail.Should().Be("antes da abertura");
    }

    [Fact]
    public void ParseMacro_FiltersDatesToWindow()
    {
        const string json = """{"release_dates":[{"release_id":10,"date":"2026-01-04"},{"release_id":10,"date":"2026-01-13"},{"release_id":10,"date":"2026-03-01"}]}""";

        IReadOnlyList<CalendarEvent> events = CalendarService.ParseMacro(new CalendarService.MacroRelease(10, "Inflação (CPI, EUA)"), json, Today, Today.AddDays(30));

        events.Should().ContainSingle().Which.Should().Be(new CalendarEvent(new DateOnly(2026, 1, 13), CalendarKind.Macro, null, "Inflação (CPI, EUA)", string.Empty));
    }

    [Fact]
    public void ForTomorrow_OnlyTomorrowsEvents_BuildCalendarAlerts()
    {
        CalendarEvent[] events =
        [
            new(Today, CalendarKind.Earnings, "TSLA", "Resultados de TSLA", string.Empty),
            new(Today.AddDays(1), CalendarKind.Earnings, "AAPL", "Resultados de AAPL", "depois do fecho"),
            new(Today.AddDays(1), CalendarKind.Macro, null, "Inflação (CPI, EUA)", string.Empty),
            new(Today.AddDays(2), CalendarKind.Macro, null, "PIB (EUA)", string.Empty)
        ];

        IReadOnlyList<Alert> alerts = CalendarAlerts.ForTomorrow(events, Today, DateTimeOffset.UtcNow);

        alerts.Select(a => a.Title).Should().Equal("Resultados amanhã: AAPL", "Amanhã: Inflação (CPI, EUA)");
        alerts.Should().OnlyContain(a => a.Kind == AlertKind.Calendar);
        alerts[0].Message.Should().Be("depois do fecho");
    }

    [Theory]
    [InlineData(0, "Hoje")]
    [InlineData(1, "Amanhã")]
    public void DayLabel_NearDays_UseNames(int offset, string expected) =>
        CalendarAlerts.DayLabel(Today.AddDays(offset), Today).Should().Be(expected);

    [Fact]
    public void DayLabel_FarDay_UsesPortugueseDate() =>
        CalendarAlerts.DayLabel(new DateOnly(2026, 1, 13), Today).Should().Contain("janeiro");

    [Fact]
    public async Task CalendarEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().GetAsync("/api/calendar")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public class CalendarInsightsTests
{
    private static JournalTradeDto Trade(string symbol, TradeDirection direction) =>
        new(1, symbol, symbol, direction, DateTimeOffset.UtcNow, 100, 1, null, null, 1, 0, string.Empty, string.Empty, null, TradeStatus.Open, null, null, null, null, null, null, null);

    [Fact]
    public void MacroKnowledge_FindsTheRightExplanation()
    {
        MacroKnowledge.Find("Inflação (CPI, EUA)")!.Importance.Should().Be(EventImportance.High);
        MacroKnowledge.Find("Vendas a retalho (EUA)")!.Importance.Should().Be(EventImportance.Medium);
        MacroKnowledge.Find("Decisão de juros da Fed (FOMC)")!.What.Should().Contain("Reserva Federal");
        MacroKnowledge.Find("Algo desconhecido").Should().BeNull();
    }

    [Fact]
    public void Exposure_RecognisesTradesAndFavorites()
    {
        Exposure exposure = Exposure.For("vz", [Trade("VZ", TradeDirection.Sell)], [], ["AAPL"]);

        exposure.HasTrade.Should().BeTrue();
        exposure.TradeDirection.Should().Be(TradeDirection.Sell);
        Exposure.For("AAPL", [], [], ["aapl"]).IsFavorite.Should().BeTrue();
        Exposure.For("MSFT", [], [], []).Any.Should().BeFalse();
    }

    [Fact]
    public void Importance_RaisesEarningsWhereTheUserIsExposed()
    {
        CalendarEvent earnings = new(new DateOnly(2026, 10, 28), CalendarKind.Earnings, "VZ", "Resultados de VZ", string.Empty);

        CalendarInsights.ImportanceOf(earnings, new Exposure(true, TradeDirection.Buy, false, false)).Should().Be(EventImportance.High);
        CalendarInsights.ImportanceOf(earnings, new Exposure(false, null, false, true)).Should().Be(EventImportance.Medium);
        CalendarInsights.ImportanceOf(earnings, Exposure.None).Should().Be(EventImportance.Low);
    }

    [Fact]
    public void TypicalDailyMove_IsTheStandardDeviationOfDailyChanges()
    {
        CalendarInsights.TypicalDailyMovePercent([100, 101, 100, 101, 100, 101, 100]).Should().BeApproximately(1.09, 0.02);
        CalendarInsights.TypicalDailyMovePercent([100, 101]).Should().BeNull();
    }

    [Fact]
    public void Formatting_UsesReadableUnits()
    {
        CalendarInsights.BigMoney(94_500_000_000).Should().Be("94,5 mil M");
        CalendarInsights.BigMoney(350_000_000).Should().Be("350 M");
    }
}
