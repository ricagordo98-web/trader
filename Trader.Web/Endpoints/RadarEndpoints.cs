using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class RadarEndpoints
{
    public static IEndpointRouteBuilder MapRadarEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/radar").RequireAuthorization();

        group.MapGet("/", async (IRadarService service, IDisplayCurrency display, CancellationToken cancellationToken) =>
            Results.Ok((await display.GetConversionAsync(cancellationToken)).Apply(service.GetSnapshot())));

        group.MapPost("/scan", async (IRadarService service, IDisplayCurrency display, CancellationToken cancellationToken) =>
        {
            await service.ScanAsync(cancellationToken);
            return Results.Ok((await display.GetConversionAsync(cancellationToken)).Apply(service.GetSnapshot()));
        });

        return app;
    }
}
