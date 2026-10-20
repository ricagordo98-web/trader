using System.Net;
using FluentAssertions;
using Trader.Core.Alerts;
using Trader.Core.Calendar;
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
              {"symbol":"AAPL","date":"2026-01-06","hour":"amc","epsEstimate":2.345},
              {"symbol":"ZZZZ","date":"2026-01-06","hour":"bmo"},
              {"symbol":"MSFT","date":"2026-01-07","hour":"bmo"}]}
            """;

        IReadOnlyList<CalendarEvent> events = CalendarService.ParseEarnings(json, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT" });

        events.Should().HaveCount(2);
        events[0].Should().Be(new CalendarEvent(new DateOnly(2026, 1, 6), CalendarKind.Earnings, "AAPL", "Resultados de AAPL", "depois do fecho · EPS esperado 2,35"));
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
