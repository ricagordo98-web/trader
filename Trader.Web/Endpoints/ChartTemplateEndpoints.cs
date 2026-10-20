using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Charts;

namespace Trader.Web.Endpoints;

public static class ChartTemplateEndpoints
{
    public static IEndpointRouteBuilder MapChartTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/chart/templates").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, IChartTemplateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(UserId(user), cancellationToken)));

        group.MapPut("/", async (List<ChartTemplate> templates, ClaimsPrincipal user, IChartTemplateService service, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await service.SaveAsync(UserId(user), templates, cancellationToken);

            return errors.Count == 0
                ? Results.NoContent()
                : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
