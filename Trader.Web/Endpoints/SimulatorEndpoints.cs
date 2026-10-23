using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Simulator;

namespace Trader.Web.Endpoints;

public static class SimulatorEndpoints
{
    public static IEndpointRouteBuilder MapSimulatorEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/simulator").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, IPaperTradingService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(UserDisplay.UserId(user), cancellationToken)));

        group.MapPost("/trades", async (OpenPaperTradeRequest request, ClaimsPrincipal user, IPaperTradingService service, CancellationToken cancellationToken) =>
        {
            (PaperTradeDto? trade, IReadOnlyList<string> errors) = await service.OpenAsync(UserDisplay.UserId(user), request, cancellationToken);
            return trade is not null ? Results.Ok(trade) : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapPost("/trades/{id:long}/close", async (long id, ClaimsPrincipal user, IPaperTradingService service, CancellationToken cancellationToken) =>
            await service.CloseAsync(UserDisplay.UserId(user), id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        group.MapPut("/account", async (PaperAccount account, ClaimsPrincipal user, IPaperTradingService service, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await service.SaveAccountAsync(UserDisplay.UserId(user), account, cancellationToken);
            return errors.Count == 0 ? Results.NoContent() : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapDelete("/", async (ClaimsPrincipal user, IPaperTradingService service, CancellationToken cancellationToken) =>
        {
            await service.ResetAsync(UserDisplay.UserId(user), cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
