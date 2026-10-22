using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Explain;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

public static class ExplainEndpoints
{
    public static IEndpointRouteBuilder MapExplainEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/explain").RequireAuthorization();

        group.MapGet("/stats", async (IMoveExplainer explainer, CancellationToken cancellationToken) =>
            Results.Ok(await explainer.GetStatsAsync(cancellationToken)));

        group.MapGet("/{symbol}", async (
            string symbol,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? refresh,
            IMoveExplainer explainer,
            IDisplayCurrency display, ClaimsPrincipal user, IUserPreferences prefs,
            CancellationToken cancellationToken) =>
        {
            try
            {
                UserPreferences preferences = await prefs.GetAsync(UserDisplay.UserId(user), cancellationToken);
                MoveExplanation? explanation = await explainer.ExplainAsync(new MoveRequest(symbol, from, to, refresh ?? false, preferences.NarrativeEnabled), cancellationToken);

                return explanation is null
                    ? Results.Problem(title: "Sem dados de mercado para este ativo e período.", statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok((await UserDisplay.ConversionAsync(user, prefs, display, cancellationToken)).Apply(explanation));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "Fonte de dados de mercado indisponível.", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        return app;
    }
}
