using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Journal;

namespace Trader.Web.Endpoints;

public static class JournalEndpoints
{
    public static IEndpointRouteBuilder MapJournalEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/journal").RequireAuthorization();

        group.MapGet("/trades", async (TradeStatus? status, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(UserId(user), status, cancellationToken)));

        group.MapPost("/trades", async (OpenTradeRequest request, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            ToResult(await service.OpenAsync(UserId(user), request, cancellationToken)));

        group.MapPost("/trades/{id:long}/close", async (long id, CloseTradeRequest request, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            ToResult(await service.CloseAsync(UserId(user), id, request, cancellationToken)));

        group.MapDelete("/trades/{id:long}", async (long id, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(UserId(user), id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/export", async (ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            Results.Text(TradeCsv.Export(await service.ListAsync(UserId(user), null, cancellationToken)), "text/csv; charset=utf-8"));

        group.MapPost("/import/preview", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            string? csv = await ReadCsvAsync(request, cancellationToken);
            return csv is null ? TooLarge() : Results.Ok(TradeCsv.Parse(csv));
        });

        group.MapPost("/import", async (HttpRequest request, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
        {
            string? csv = await ReadCsvAsync(request, cancellationToken);

            if (csv is null)
            {
                return TooLarge();
            }

            ImportPreview preview = TradeCsv.Parse(csv);
            return preview.Trades.Count == 0
                ? Results.Problem(title: preview.Warnings.FirstOrDefault() ?? "Nenhuma trade reconhecida.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(await service.ImportAsync(UserId(user), preview.Trades, cancellationToken));
        });

        group.MapGet("/stats", async (ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetStatsAsync(UserId(user), cancellationToken)));

        group.MapGet("/account", async (ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAccountAsync(UserId(user), cancellationToken)));

        group.MapPut("/account", async (AccountProfile profile, ClaimsPrincipal user, IJournalService service, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<string> errors = await service.SaveAccountAsync(UserId(user), profile, cancellationToken);

            return errors.Count == 0
                ? Results.NoContent()
                : Results.Problem(title: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private const int MaxCsvBytes = 2 * 1024 * 1024;

    private static async Task<string?> ReadCsvAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxCsvBytes)
        {
            return null;
        }

        using StreamReader reader = new(request.Body);
        string text = await reader.ReadToEndAsync(cancellationToken);
        return text.Length > MaxCsvBytes ? null : text;
    }

    private static IResult TooLarge() => Results.Problem(title: "O ficheiro é demasiado grande (máx. 2 MB).", statusCode: StatusCodes.Status413PayloadTooLarge);

    private static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user without an identifier.");

    private static IResult ToResult(JournalResult<JournalTradeDto> result)
    {
        if (result.NotFound)
        {
            return Results.NotFound();
        }

        return result.Value is { } trade && result.Errors.Count == 0
            ? Results.Ok(trade)
            : Results.Problem(title: string.Join(" ", result.Errors), statusCode: StatusCodes.Status400BadRequest);
    }
}
