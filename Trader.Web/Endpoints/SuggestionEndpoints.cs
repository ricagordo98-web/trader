using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class SuggestionEndpoints
{
    public static IEndpointRouteBuilder MapSuggestionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/suggestions").RequireAuthorization();

        group.MapGet("/", async (string? symbol, bool? open, int? limit, SuggestionHorizon? horizon, bool? native, bool? shadow, ISuggestionService service, IInstrumentService instruments, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
        {
            UserPreferences profile = await prefs.GetAsync(UserDisplay.UserId(user), cancellationToken);
            IReadOnlyList<SuggestionDto> suggestions = await service.GetAsync(symbol, open, limit ?? 100, horizon, cancellationToken, shadow ?? profile.ShowShadowSuggestions);

            if (profile.MinDisplayedConfidence > 0)
            {
                suggestions = suggestions.Where(s => s.IsShadow || (s.CalibratedConfidence ?? s.Confidence) >= profile.MinDisplayedConfidence).ToList();
            }

            if (native == true)
            {
                return Results.Ok(suggestions);
            }

            HashSet<string>? allowed = await UserDisplay.AllowedSymbolsAsync(profile, instruments, cancellationToken);

            if (allowed is not null)
            {
                suggestions = suggestions.Where(s => allowed.Contains(s.Symbol)).ToList();
            }

            DisplayConversion conversion = await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken);
            return Results.Ok(suggestions.Select(conversion.Apply).ToList());
        });

        group.MapGet("/track-record", async (SuggestionHorizon? horizon, ISuggestionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetTrackRecordAsync(horizon, cancellationToken)));

        group.MapGet("/reliability", async (SuggestionHorizon? horizon, IModelReliabilityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetReportAsync(horizon, cancellationToken)));

        group.MapGet("/reliability/weights", async (SuggestionHorizon? horizon, IModelReliabilityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetWeightLearningAsync(horizon ?? SuggestionHorizon.Intraday, cancellationToken)));

        group.MapPost("/reliability/weights/apply", async (SuggestionHorizon? horizon, IModelReliabilityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ApplyLearnedWeightsAsync(horizon ?? SuggestionHorizon.Intraday, cancellationToken)));

        group.MapGet("/reliability/meta", async (SuggestionHorizon? horizon, IModelReliabilityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMetaModelAsync(horizon ?? SuggestionHorizon.Intraday, cancellationToken)));

        group.MapPost("/reliability/meta/apply", async (SuggestionHorizon? horizon, IModelReliabilityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ApplyMetaModelAsync(horizon ?? SuggestionHorizon.Intraday, cancellationToken)));

        group.MapDelete("/reliability/meta", async (SuggestionHorizon? horizon, IModelReliabilityService service, CancellationToken cancellationToken) =>
        {
            await service.DisableMetaModelAsync(horizon ?? SuggestionHorizon.Intraday, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/reliability/sentiment", async (IModelReliabilityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSentimentStudyAsync(cancellationToken)));

        group.MapPost("/refresh", async (ISuggestionService service, CancellationToken cancellationToken) =>
            Results.Ok(new { created = await service.RefreshAsync(cancellationToken) }));

        return app;
    }
}
