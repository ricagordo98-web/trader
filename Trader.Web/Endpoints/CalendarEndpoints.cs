using Trader.Core.Abstractions;

namespace Trader.Web.Endpoints;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/calendar", async (int? days, ICalendarService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(days ?? 14, cancellationToken))).RequireAuthorization();

        return app;
    }
}
