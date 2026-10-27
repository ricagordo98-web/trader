using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Web.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/watchlist", async (IInstrumentService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(cancellationToken))
                .Select(i => new WatchlistItem(i.Symbol, i.Name, i.AssetClass, i.Currency))));

        group.MapPost("/watchlist", async (AddInstrumentRequest request, IInstrumentService service, CancellationToken cancellationToken) =>
        {
            AddInstrumentResult result = await service.AddAsync(request, cancellationToken);

            return result.Instrument is { } instrument
                ? Results.Ok(new WatchlistItem(instrument.Symbol, instrument.Name, instrument.AssetClass, instrument.Currency))
                : Results.Problem(title: result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapDelete("/watchlist/{symbol}", async (string symbol, IInstrumentService service, CancellationToken cancellationToken) =>
            await service.RemoveAsync(symbol, cancellationToken) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/settings/display-currency", async (IDisplayCurrency display, CancellationToken cancellationToken) =>
            Results.Ok(new DisplayCurrencySettings(await display.GetAsync(cancellationToken))));

        group.MapPut("/settings/display-currency", async (DisplayCurrencySettings request, IDisplayCurrency display, CancellationToken cancellationToken) =>
        {
            string? error = await display.SetAsync(request.Currency, cancellationToken);
            return error is null ? Results.NoContent() : Results.Problem(title: error, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapGet("/settings/profile", async (ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
            Results.Ok(await prefs.GetAsync(UserDisplay.UserId(user), cancellationToken)));

        group.MapPut("/settings/profile", async (UserPreferences request, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await prefs.SaveAsync(UserDisplay.UserId(user), request, cancellationToken);
            return errors.Count == 0 ? Results.NoContent() : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapGet("/settings/price-source", async (IPriceSettings prices, CancellationToken cancellationToken) =>
            Results.Ok(new PriceSourceSettings(await prices.GetCryptoBasisAsync(cancellationToken))));

        group.MapPut("/settings/price-source", async (PriceSourceSettings request, IPriceSettings prices, CancellationToken cancellationToken) =>
        {
            string? error = await prices.SetCryptoBasisAsync(request.CryptoBasis, cancellationToken);
            return error is null ? Results.NoContent() : Results.Problem(title: error, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapGet("/sources", (ISourceStatusRegistry registry) => Results.Ok(registry.GetAll()));

        group.MapGet("/settings/scoring", async (IScoringSettings settings, CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        group.MapPut("/settings/scoring", async (ScoringOptions options, IScoringSettings settings, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = ScoringValidator.Validate(options);

            if (errors.Count > 0)
            {
                return Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
            }

            await settings.SaveAsync(options, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/settings/intraday", async (IIntradaySettings settings, CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        group.MapPut("/settings/intraday", async ([Microsoft.AspNetCore.Mvc.FromBody] Trader.Core.Intraday.IntradayOptions options, IIntradaySettings settings, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await settings.SaveAsync(options, cancellationToken);

            return errors.Count == 0
                ? Results.NoContent()
                : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapGet("/settings/intraday/variant-b", async (IIntradaySettings settings, CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetVariantBAsync(cancellationToken)));

        group.MapPut("/settings/intraday/variant-b", async ([Microsoft.AspNetCore.Mvc.FromBody] VariantSettings variant, IIntradaySettings settings, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await settings.SaveVariantBAsync(variant, cancellationToken);

            return errors.Count == 0
                ? Results.NoContent()
                : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapDelete("/settings/intraday/variant-b", async (IIntradaySettings settings, CancellationToken cancellationToken) =>
        {
            await settings.ResetVariantBAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapDelete("/settings/intraday", async (IIntradaySettings settings, CancellationToken cancellationToken) =>
        {
            await settings.ResetAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapDelete("/settings/scoring", async (IScoringSettings settings, CancellationToken cancellationToken) =>
        {
            await settings.ResetAsync(cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
