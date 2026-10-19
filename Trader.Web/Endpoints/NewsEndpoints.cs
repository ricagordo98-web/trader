using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class NewsEndpoints
{
    public static IEndpointRouteBuilder MapNewsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/news").RequireAuthorization();

        group.MapGet("/", async (
            string? symbol,
            SourceType? sourceType,
            string? sentiment,
            string? search,
            DateTimeOffset? before,
            int? limit,
            INewsService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetArticlesAsync(
                new NewsQuery(symbol, sourceType, sentiment, search, before, limit ?? 50),
                cancellationToken)));

        group.MapGet("/sentiment", async (int? hours, INewsService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSentimentSummaryAsync(TimeSpan.FromHours(Math.Clamp(hours ?? 24, 1, 168)), cancellationToken)));

        return app;
    }
}
