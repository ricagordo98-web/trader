using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;

namespace Trader.Web.Endpoints;

public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/alerts").RequireAuthorization();

        group.MapGet("/", async (int? limit, ClaimsPrincipal user, IAlertService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetFeedAsync(UserId(user), limit ?? 30, cancellationToken)));

        group.MapPost("/seen", async (ClaimsPrincipal user, IAlertService service, CancellationToken cancellationToken) =>
        {
            await service.MarkSeenAsync(UserId(user), cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/ntfy", async (IAlertService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetNtfyAsync(cancellationToken)));

        group.MapPut("/ntfy", async (NtfySettings settings, IAlertService service, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await service.SaveNtfyAsync(settings, cancellationToken);

            return errors.Count == 0
                ? Results.NoContent()
                : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapPost("/ntfy/test", async (IAlertService service, CancellationToken cancellationToken) =>
        {
            string? error = await service.SendTestAsync(cancellationToken);

            return error is null
                ? Results.NoContent()
                : Results.Problem(title: error, statusCode: StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
