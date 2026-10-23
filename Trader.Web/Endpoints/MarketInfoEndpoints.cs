using Trader.Core.Market;
using Trader.Infrastructure.MarketData;

namespace Trader.Web.Endpoints;

public static class MarketInfoEndpoints
{
    public static IEndpointRouteBuilder MapMarketInfoEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/market").RequireAuthorization();

        group.MapGet("/sessions", () => Results.Ok(MarketSessions.All(DateTimeOffset.UtcNow)));

        group.MapGet("/halts", (HaltMonitor monitor) => Results.Ok(monitor.Halts.Where(h => h.HaltedAt > DateTimeOffset.UtcNow.AddHours(-12)).Take(100).ToList()));

        group.MapGet("/profiles", async (string symbols, ICompanyProfileService profiles, CancellationToken cancellationToken) =>
            Results.Ok(await profiles.GetAsync(symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), cancellationToken)));

        group.MapGet("/flow/{symbol}", (string symbol, IOrderFlowService flow) => Results.Ok(flow.Get(symbol)));

        return app;
    }
}
