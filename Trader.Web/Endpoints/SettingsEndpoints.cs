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

        group.MapDelete("/settings/scoring", async (IScoringSettings settings, CancellationToken cancellationToken) =>
        {
            await settings.ResetAsync(cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
