using System.Text.Json;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class MarketEndpoints
{
    private static readonly HashSet<string> AllowedIntervals = ["1m", "5m", "15m", "30m", "60m", "1d", "1wk", "1mo"];
    private static readonly HashSet<string> AllowedRanges = ["1d", "5d", "1mo", "3mo", "6mo", "1y", "2y", "5y", "10y", "max"];

    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/instruments", async (AssetClass? assetClass, IMarketDataService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetQuotesAsync(assetClass, cancellationToken)));

        group.MapGet("/instruments/{symbol}/candles", async (
            string symbol,
            string? interval,
            string? range,
            IMarketDataService service,
            CancellationToken cancellationToken) =>
        {
            string resolvedInterval = interval ?? "1d";
            string resolvedRange = range ?? "1y";

            if (!AllowedIntervals.Contains(resolvedInterval) || !AllowedRanges.Contains(resolvedRange))
            {
                return Results.Problem(
                    title: "Invalid interval or range",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                return Results.Ok(await service.GetCandlesAsync(symbol, resolvedInterval, resolvedRange, cancellationToken));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    title: "Market data provider unavailable",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/instruments/{symbol}/drawings", async (string symbol, IChartDrawingService service, CancellationToken cancellationToken) =>
            Results.Content(await service.GetAsync(symbol, cancellationToken), "application/json"));

        group.MapPut("/instruments/{symbol}/drawings", async (string symbol, HttpRequest request, IChartDrawingService service, CancellationToken cancellationToken) =>
        {
            using StreamReader reader = new(request.Body);
            string json = await reader.ReadToEndAsync(cancellationToken);

            try
            {
                JsonDocument.Parse(json).Dispose();
            }
            catch (JsonException)
            {
                return Results.Problem(title: "Invalid drawings payload", statusCode: StatusCodes.Status400BadRequest);
            }

            await service.SaveAsync(symbol, json, cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
