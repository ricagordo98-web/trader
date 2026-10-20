using System.Text.Json;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class MarketEndpoints
{
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

            if (!ChartParameters.IsValid(resolvedInterval, resolvedRange))
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

        group.MapGet("/instruments/{symbol}/live", async (
            string symbol,
            string interval,
            string range,
            IMarketDataService service,
            CancellationToken cancellationToken) =>
        {
            if (!ChartParameters.IsValid(interval, range))
            {
                return Results.Problem(title: "Invalid interval or range", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                return Results.Ok(await service.GetLiveCandlesAsync(symbol, interval, range, cancellationToken));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "Market data provider unavailable", statusCode: StatusCodes.Status502BadGateway);
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
