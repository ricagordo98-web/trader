using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class SuggestionEndpoints
{
    public static IEndpointRouteBuilder MapSuggestionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/suggestions").RequireAuthorization();

        group.MapGet("/", async (string? symbol, bool? open, int? limit, SuggestionHorizon? horizon, bool? native, ISuggestionService service, IInstrumentService instruments, IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<SuggestionDto> suggestions = await service.GetAsync(symbol, open, limit ?? 100, horizon, cancellationToken);

            if (native == true)
            {
                return Results.Ok(suggestions);
            }

            HashSet<string>? allowed = await UserDisplay.AllowedSymbolsAsync(await prefs.GetAsync(UserDisplay.UserId(user), cancellationToken), instruments, cancellationToken);

            if (allowed is not null)
            {
                suggestions = suggestions.Where(s => allowed.Contains(s.Symbol)).ToList();
            }

            DisplayConversion conversion = await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken);
            return Results.Ok(suggestions.Select(conversion.Apply).ToList());
        });

        group.MapGet("/track-record", async (SuggestionHorizon? horizon, ISuggestionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetTrackRecordAsync(horizon, cancellationToken)));

        group.MapPost("/refresh", async (ISuggestionService service, CancellationToken cancellationToken) =>
            Results.Ok(new { created = await service.RefreshAsync(cancellationToken) }));

        return app;
    }
}
