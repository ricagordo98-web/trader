using Trader.Core.Abstractions;
using Trader.Core.Summary;

namespace Trader.Web.Endpoints;

public static class SummaryEndpoints
{
    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/summary").RequireAuthorization();

        group.MapGet("/", async (IDailySummaryService service, CancellationToken cancellationToken) =>
            await service.GetLastAsync(cancellationToken) is { } summary ? Results.Ok(summary) : Results.NoContent());

        group.MapPost("/generate", async (IDailySummaryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GenerateAsync(cancellationToken)));

        return app;
    }
}
