using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class SuggestionEndpoints
{
    public static IEndpointRouteBuilder MapSuggestionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/suggestions").RequireAuthorization();

        group.MapGet("/", async (string? symbol, bool? open, int? limit, SuggestionHorizon? horizon, ISuggestionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(symbol, open, limit ?? 100, horizon, cancellationToken)));

        group.MapGet("/track-record", async (SuggestionHorizon? horizon, ISuggestionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetTrackRecordAsync(horizon, cancellationToken)));

        group.MapPost("/refresh", async (ISuggestionService service, CancellationToken cancellationToken) =>
            Results.Ok(new { created = await service.RefreshAsync(cancellationToken) }));

        return app;
    }
}
