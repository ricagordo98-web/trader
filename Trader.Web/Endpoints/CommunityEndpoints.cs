using Trader.Core.Abstractions;

namespace Trader.Web.Endpoints;

public static class CommunityEndpoints
{
    public static IEndpointRouteBuilder MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/community").RequireAuthorization();

        group.MapGet("/trending", async (ICommunityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetTrendingAsync(cancellationToken)));

        return app;
    }
}
