using System.Security.Claims;
using Trader.Core.Abstractions;

namespace Trader.Web.Endpoints;

public static class ChartLayoutEndpoints
{
    public sealed record SaveLayoutRequest(string Name, string Json);

    public static IEndpointRouteBuilder MapChartLayoutEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/chart-layouts").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, IChartLayoutService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(UserDisplay.UserId(user), cancellationToken)));

        group.MapPut("/", async (SaveLayoutRequest request, ClaimsPrincipal user, IChartLayoutService service, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await service.SaveAsync(UserDisplay.UserId(user), request.Name, request.Json, cancellationToken);
            return errors.Count == 0 ? Results.NoContent() : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapDelete("/{name}", async (string name, ClaimsPrincipal user, IChartLayoutService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(UserDisplay.UserId(user), name, cancellationToken) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}
