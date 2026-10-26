using System.Globalization;
using Trader.Core.Alerts;

namespace Trader.Core.Calendar;

public enum CalendarKind
{
    Earnings,
    Macro
}

public sealed record CalendarEvent(DateOnly Date, CalendarKind Kind, string? Symbol, string Title, string Detail, double? EpsEstimate = null, double? RevenueEstimate = null, string Period = "");

public sealed record CalendarFeed(IReadOnlyList<CalendarEvent> Events, IReadOnlyList<string> Notes);

public static class CalendarAlerts
{
    public static IReadOnlyList<Alert> ForTomorrow(IEnumerable<CalendarEvent> events, DateOnly today, DateTimeOffset now) =>
        events
            .Where(e => e.Date == today.AddDays(1))
            .Select(e => new Alert
            {
                Kind = AlertKind.Calendar,
                Symbol = e.Symbol ?? "MACRO",
                Title = e.Kind == CalendarKind.Earnings ? $"Resultados amanhã: {e.Symbol}" : $"Amanhã: {e.Title}",
                Message = e.Detail.Length > 0 ? e.Detail : e.Title,
                CreatedAt = now
            })
            .ToList();

    public static string DayLabel(DateOnly date, DateOnly today)
    {
        int offset = date.DayNumber - today.DayNumber;

        return offset switch
        {
            0 => "Hoje",
            1 => "Amanhã",
            _ => date.ToString("dddd, d 'de' MMMM", CultureInfo.GetCultureInfo("pt-PT"))
        };
    }
}
