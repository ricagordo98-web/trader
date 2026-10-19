using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trader.Infrastructure.Persistence;

namespace Trader.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder auth = app.MapGroup("/api/auth");

        auth.AddEndpointFilter(RestrictRegistrationAsync);
        auth.MapIdentityApi<AppUser>();

        auth.MapGet("/status", async (UserManager<AppUser> users, CancellationToken cancellationToken) =>
            Results.Ok(new { registrationOpen = !await users.Users.AnyAsync(cancellationToken) }));

        return app;
    }

    private static async ValueTask<object?> RestrictRegistrationAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;

        if (http.Request.Path.Value?.EndsWith("/register", StringComparison.OrdinalIgnoreCase) == true)
        {
            UserManager<AppUser> users = http.RequestServices.GetRequiredService<UserManager<AppUser>>();
            bool hasUsers = await users.Users.AnyAsync(http.RequestAborted);

            if (hasUsers && http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Problem(
                    title: "O registo está fechado. Pede a um utilizador com sessão iniciada para criar a tua conta.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return await next(context);
    }
}
