using Trader.Core.Abstractions;
using Trader.Core.Maintenance;

namespace Trader.Web.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/system").RequireAuthorization();

        group.MapGet("/backups", async (IBackupService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        group.MapPost("/backups", async (IBackupService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.CreateAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                return Results.Problem(title: "Não foi possível criar o backup: " + ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        return app;
    }
}
