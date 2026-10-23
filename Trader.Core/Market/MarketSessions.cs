namespace Trader.Core.Market;

public sealed record SessionStatus(string Name, string Region, bool IsOpen, DateTimeOffset NextChange, string Detail);

public static class MarketSessions
{
    private sealed record Definition(string Name, string Region, string IanaZone, string WindowsZone, TimeOnly Open, TimeOnly Close);

    private static readonly Definition[] Definitions =
    [
        new("EUA (NYSE/Nasdaq)", "us", "America/New_York", "Eastern Standard Time", new TimeOnly(9, 30), new TimeOnly(16, 0)),
        new("Europa (Euronext)", "eu", "Europe/Paris", "Romance Standard Time", new TimeOnly(9, 0), new TimeOnly(17, 30)),
        new("Ásia (Tóquio)", "asia", "Asia/Tokyo", "Tokyo Standard Time", new TimeOnly(9, 0), new TimeOnly(15, 0))
    ];

    public static IReadOnlyList<SessionStatus> All(DateTimeOffset now) =>
    [
        .. Definitions.Select(d => Status(d, now)),
        Forex(now),
        new SessionStatus("Cripto", "crypto", true, now.AddYears(1), "Aberto 24 horas, todos os dias")
    ];

    private static SessionStatus Status(Definition definition, DateTimeOffset now)
    {
        TimeZoneInfo zone = Zone(definition.IanaZone, definition.WindowsZone);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, zone);
        bool weekday = local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        TimeOnly time = TimeOnly.FromDateTime(local.DateTime);
        bool open = weekday && time >= definition.Open && time < definition.Close;

        DateTimeOffset next = open
            ? AtLocal(local.Date, definition.Close, zone)
            : AtLocal(NextWeekday(local.Date, weekday && time < definition.Open), definition.Open, zone);

        string detail = open
            ? $"Aberto até às {definition.Close:HH\\:mm} (hora local), fecha {Remaining(next - now)}"
            : $"Fechado, abre {Remaining(next - now)}";

        return new SessionStatus(definition.Name, definition.Region, open, next, detail);
    }

    private static SessionStatus Forex(DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        bool open = utc.DayOfWeek switch
        {
            DayOfWeek.Saturday => false,
            DayOfWeek.Sunday => utc.Hour >= 22,
            DayOfWeek.Friday => utc.Hour < 22,
            _ => true
        };

        DateTimeOffset next;

        if (open)
        {
            int daysToFriday = ((int)DayOfWeek.Friday - (int)utc.DayOfWeek + 7) % 7;
            next = new DateTimeOffset(utc.Date.AddDays(daysToFriday).AddHours(22), TimeSpan.Zero);
        }
        else
        {
            int daysToSunday = ((int)DayOfWeek.Sunday - (int)utc.DayOfWeek + 7) % 7;
            next = new DateTimeOffset(utc.Date.AddDays(daysToSunday).AddHours(22), TimeSpan.Zero);
        }

        return new SessionStatus(
            "Forex",
            "forex",
            open,
            next,
            open ? $"Aberto de domingo às 22:00 a sexta às 22:00 (UTC), fecha {Remaining(next - now)}" : $"Fechado, abre {Remaining(next - now)}");
    }

    public static string Remaining(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalDays >= 2)
        {
            return $"em {(int)span.TotalDays} dias";
        }

        if (span.TotalHours >= 1)
        {
            return $"em {(int)span.TotalHours} h {span.Minutes:00} min";
        }

        return span.TotalMinutes >= 1 ? $"em {(int)span.TotalMinutes} min" : "em instantes";
    }

    private static DateTime NextWeekday(DateTime date, bool today)
    {
        DateTime day = today ? date : date.AddDays(1);

        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private static DateTimeOffset AtLocal(DateTime date, TimeOnly time, TimeZoneInfo zone)
    {
        DateTime unspecified = DateTime.SpecifyKind(date.Date + time.ToTimeSpan(), DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    public static TimeZoneInfo Zone(string iana, string windows)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(iana);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windows);
        }
    }
}
