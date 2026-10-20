using Trader.Core.Calendar;

namespace Trader.Core.Abstractions;

public interface ICalendarService
{
    Task<CalendarFeed> GetAsync(int days, CancellationToken cancellationToken);
}
