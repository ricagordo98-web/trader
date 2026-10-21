using Trader.Core.Abstractions;

namespace Trader.Web.Endpoints;

public static class RadarEndpoints
{
    public static IEndpointRouteBuilder MapRadarEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/radar").RequireAuthorization();

        group.MapGet("/", (IRadarService service) => Results.Ok(service.GetSnapshot()));

        group.MapPost("/scan", async (IRadarService service, CancellationToken cancellationToken) =>
        {
            await service.ScanAsync(cancellationToken);
            return Results.Ok(service.GetSnapshot());
        });

        return app;
    }
}
