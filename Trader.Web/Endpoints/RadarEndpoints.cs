using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Intraday;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class RadarEndpoints
{
    public static IEndpointRouteBuilder MapRadarEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/radar").RequireAuthorization();

        group.MapGet("/", async (IRadarService service, IInstrumentService instruments, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
            Results.Ok(await ForUserAsync(service.GetSnapshot(), instruments, display, user, prefs, cancellationToken)));

        group.MapPost("/scan", async (IRadarService service, IInstrumentService instruments, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
        {
            await service.ScanAsync(cancellationToken);
            return Results.Ok(await ForUserAsync(service.GetSnapshot(), instruments, display, user, prefs, cancellationToken));
        });

        return app;
    }

    private static async Task<RadarSnapshot> ForUserAsync(
        RadarSnapshot snapshot,
        IInstrumentService instruments,
        IDisplayCurrency display,
        ClaimsPrincipal user,
        IUserPreferences prefs,
        CancellationToken cancellationToken)
    {
        UserPreferences preferences = await prefs.GetAsync(UserDisplay.UserId(user), cancellationToken);
        HashSet<string>? allowed = await UserDisplay.AllowedSymbolsAsync(preferences, instruments, cancellationToken);

        if (allowed is not null)
        {
            snapshot = snapshot with
            {
                Events = snapshot.Events.Where(e => allowed.Contains(e.Symbol)).ToList(),
                Board = snapshot.Board.Where(r => allowed.Contains(r.Symbol)).ToList()
            };
        }

        return (await display.GetConversionAsync(preferences, cancellationToken)).Apply(snapshot);
    }
}
