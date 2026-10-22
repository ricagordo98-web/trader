using Trader.Core.Abstractions;
using Trader.Core.Backtesting;

namespace Trader.Web.Endpoints;

public static class BacktestEndpoints
{
    public static IEndpointRouteBuilder MapBacktestEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/backtest").RequireAuthorization();

        group.MapPost("/", async (BacktestRequest request, IBacktestService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.RunAsync(request, cancellationToken));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapGet("/last", async (IBacktestService service, CancellationToken cancellationToken) =>
            await service.GetLastAsync(cancellationToken) is { } result ? Results.Ok(result) : Results.NoContent());

        group.MapPost("/intraday", async (IntradayBacktestRequest request, IIntradayBacktestService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.RunAsync(request, cancellationToken));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapGet("/intraday/last", async (IIntradayBacktestService service, CancellationToken cancellationToken) =>
            await service.GetLastAsync(cancellationToken) is { } result ? Results.Ok(result) : Results.NoContent());

        return app;
    }
}
