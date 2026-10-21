using System.Text.Json;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/instruments", async (AssetClass? assetClass, bool? native, IMarketDataService service, IDisplayCurrency display, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<InstrumentQuote> quotes = await service.GetQuotesAsync(assetClass, cancellationToken);

            if (native == true)
            {
                return Results.Ok(quotes);
            }

            DisplayConversion conversion = await display.GetConversionAsync(cancellationToken);
            return Results.Ok(quotes.Select(conversion.Apply).ToList());
        });

        group.MapGet("/instruments/{symbol}/candles", async (
            string symbol,
            string? interval,
            string? range,
            IMarketDataService service,
            IDisplayCurrency display,
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
                IReadOnlyList<Candle> candles = await service.GetCandlesAsync(symbol, resolvedInterval, resolvedRange, cancellationToken);
                return Results.Ok((await display.GetConversionAsync(cancellationToken)).Apply(symbol, candles));
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
            IDisplayCurrency display,
            CancellationToken cancellationToken) =>
        {
            if (!ChartParameters.IsValid(interval, range))
            {
                return Results.Problem(title: "Invalid interval or range", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                IReadOnlyList<Candle> candles = await service.GetLiveCandlesAsync(symbol, interval, range, cancellationToken);
                return Results.Ok((await display.GetConversionAsync(cancellationToken)).Apply(symbol, candles));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "Market data provider unavailable", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/instruments/{symbol}/drawings", async (string symbol, IChartDrawingService service, IDisplayCurrency display, CancellationToken cancellationToken) =>
        {
            string json = await service.GetAsync(symbol, cancellationToken);
            return Results.Content((await display.GetConversionAsync(cancellationToken)).ToDisplayDrawings(symbol, json), "application/json");
        });

        group.MapPut("/instruments/{symbol}/drawings", async (string symbol, HttpRequest request, IChartDrawingService service, IDisplayCurrency display, CancellationToken cancellationToken) =>
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

            await service.SaveAsync(symbol, (await display.GetConversionAsync(cancellationToken)).ToStoredDrawings(symbol, json), cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
