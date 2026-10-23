using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;

namespace Trader.Web.Endpoints;

public static class AlertRuleEndpoints
{
    public static IEndpointRouteBuilder MapAlertRuleEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/alert-rules").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, IAlertRuleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(UserDisplay.UserId(user), cancellationToken)));

        group.MapPut("/", async (AlertRuleDto rule, ClaimsPrincipal user, IAlertRuleService service, CancellationToken cancellationToken) =>
        {
            (AlertRuleDto? saved, IReadOnlyList<string> errors) = await service.SaveAsync(UserDisplay.UserId(user), rule, cancellationToken);
            return saved is not null ? Results.Ok(saved) : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, IAlertRuleService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(UserDisplay.UserId(user), id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}
