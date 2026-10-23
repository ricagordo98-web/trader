using Trader.Core.Market;

namespace Trader.Core.Calendar;

public sealed record TimedEvent(CalendarEvent Event, DateTimeOffset Time);

public static class CalendarTimes
{
    private static readonly TimeOnly UsMorning = new(8, 30);
    private static readonly TimeOnly FomcDecision = new(14, 0);

    public static DateTimeOffset? TimeFor(CalendarEvent calendarEvent)
    {
        if (calendarEvent.Kind == CalendarKind.Earnings)
        {
            return calendarEvent.Detail switch
            {
                var d when d.Contains("antes da abertura", StringComparison.OrdinalIgnoreCase) => Utc(calendarEvent.Date, new TimeOnly(11, 30)),
                var d when d.Contains("depois do fecho", StringComparison.OrdinalIgnoreCase) => Utc(calendarEvent.Date, new TimeOnly(21, 0)),
                var d when d.Contains("durante a sessão", StringComparison.OrdinalIgnoreCase) => Utc(calendarEvent.Date, new TimeOnly(15, 0)),
                _ => null
            };
        }

        TimeOnly? local = calendarEvent.Title switch
        {
            var t when t.Contains("FOMC", StringComparison.OrdinalIgnoreCase) => FomcDecision,
            var t when t.Contains("CPI", StringComparison.OrdinalIgnoreCase)
                || t.Contains("Non-Farm", StringComparison.OrdinalIgnoreCase)
                || t.Contains("PIB", StringComparison.OrdinalIgnoreCase)
                || t.Contains("PCE", StringComparison.OrdinalIgnoreCase)
                || t.Contains("PPI", StringComparison.OrdinalIgnoreCase)
                || t.Contains("retalho", StringComparison.OrdinalIgnoreCase) => UsMorning,
            _ => null
        };

        if (local is not { } time)
        {
            return null;
        }

        TimeZoneInfo zone = MarketSessions.Zone("America/New_York", "Eastern Standard Time");
        DateTime unspecified = DateTime.SpecifyKind(calendarEvent.Date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    public static IReadOnlyList<TimedEvent> Upcoming(IEnumerable<CalendarEvent> events, DateTimeOffset now, TimeSpan window) =>
        events
            .Select(e => TimeFor(e) is { } time ? new TimedEvent(e, time) : null)
            .OfType<TimedEvent>()
            .Where(t => t.Time >= now - TimeSpan.FromMinutes(30) && t.Time <= now + window)
            .OrderBy(t => t.Time)
            .ToList();

    public static TimedEvent? NextMacro(IEnumerable<CalendarEvent> events, DateTimeOffset now, TimeSpan window) =>
        Upcoming(events.Where(e => e.Kind == CalendarKind.Macro), now, window).FirstOrDefault();

    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), TimeSpan.Zero);
}
