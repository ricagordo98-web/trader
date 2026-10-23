using System.Security.Claims;
using System.Text.Json;
using Trader.Core.Charts;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/instruments", async (AssetClass? assetClass, bool? native, IMarketDataService service, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
        {
            UserPreferences preferences = await prefs.GetAsync(UserDisplay.UserId(user), cancellationToken);
            IReadOnlyList<InstrumentQuote> quotes = (await service.GetQuotesAsync(assetClass, cancellationToken)).Where(q => native == true || preferences.Allows(q.AssetClass)).ToList();

            if (native == true)
            {
                return Results.Ok(quotes);
            }

            DisplayConversion conversion = await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken);
            return Results.Ok(quotes.Select(conversion.Apply).ToList());
        });

        group.MapGet("/instruments/{symbol}/candles", async (
            string symbol,
            string? interval,
            string? range,
            IMarketDataService service,
            IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs,
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
                return Results.Ok((await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken)).Apply(symbol, candles));
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
            IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs,
            CancellationToken cancellationToken) =>
        {
            if (!ChartParameters.IsValid(interval, range))
            {
                return Results.Problem(title: "Invalid interval or range", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                IReadOnlyList<Candle> candles = await service.GetLiveCandlesAsync(symbol, interval, range, cancellationToken);
                return Results.Ok((await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken)).Apply(symbol, candles));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "Market data provider unavailable", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/instruments/{symbol}/levels", async (string symbol, IChartInsights insights, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
        {
            try
            {
                DisplayConversion conversion = await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken);
                double factor = conversion.FactorFor(symbol);
                return Results.Ok((await insights.GetLevelsAsync(symbol, cancellationToken)).Select(l => l with { Price = l.Price * factor }).ToList());
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "Market data provider unavailable", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/instruments/{symbol}/events", async (string symbol, DateTimeOffset from, DateTimeOffset to, IChartInsights insights, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            Results.Ok(await insights.GetEventsAsync(symbol, UserDisplay.UserId(user), from, to, cancellationToken)));

        group.MapGet("/instruments/{symbol}/drawings", async (string symbol, IChartDrawingService service, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
        {
            string json = await service.GetAsync(symbol, cancellationToken);
            return Results.Content((await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken)).ToDisplayDrawings(symbol, json), "application/json");
        });

        group.MapPut("/instruments/{symbol}/drawings", async (string symbol, HttpRequest request, IChartDrawingService service, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
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

            await service.SaveAsync(symbol, (await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken)).ToStoredDrawings(symbol, json), cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
