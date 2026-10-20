using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Journal;

namespace Trader.Web.Endpoints;

public static class JournalEndpoints
{
    public static IEndpointRouteBuilder MapJournalEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/journal").RequireAuthorization();

        group.MapGet("/trades", async (TradeStatus? status, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(UserId(user), status, cancellationToken)));

        group.MapPost("/trades", async (OpenTradeRequest request, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            ToResult(await service.OpenAsync(UserId(user), request, cancellationToken)));

        group.MapPost("/trades/{id:long}/close", async (long id, CloseTradeRequest request, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            ToResult(await service.CloseAsync(UserId(user), id, request, cancellationToken)));

        group.MapDelete("/trades/{id:long}", async (long id, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(UserId(user), id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/stats", async (ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetStatsAsync(UserId(user), cancellationToken)));

        group.MapGet("/account", async (ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAccountAsync(UserId(user), cancellationToken)));

        group.MapPut("/account", async (AccountProfile profile, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await service.SaveAccountAsync(UserId(user), profile, cancellationToken);

            return errors.Count == 0
                ? Results.NoContent()
                : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user without an identifier.");

    private static IResult ToResult(JournalResult<JournalTradeDto> result)
    {
        if (result.NotFound)
        {
            return Results.NotFound();
        }

        return result.Value is { } trade && result.Errors.Count == 0
            ? Results.Ok(trade)
            : Results.Problem(title: string.Join(" ", result.Errors), statusCode: StatusCodes.Status400BadRequest);
    }
}
