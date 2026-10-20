using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Infrastructure;
using Trader.Infrastructure.Persistence;
using Trader.Shared.Extensions;
using Trader.Shared.Services;
using Trader.Web.Components;
using Trader.Web.Endpoints;
using Trader.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTraderShared();
builder.Services.AddTraderInfrastructure(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, "models", "finbert"));
builder.Services.AddSignalR();
builder.Services.AddSingleton<LiveSubscriptions>();
builder.Services.AddHostedService<LiveCandlePublisher>();
builder.Services.AddSingleton<INewsNotifier, SignalRNewsNotifier>();
builder.Services.AddSingleton<IAlertNotifier, SignalRAlertNotifier>();

builder.Services.AddIdentityApiEndpoints<AppUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<TraderDbContext>();

builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.Events.OnMessageReceived = context =>
    {
        string? token = context.Request.Query["access_token"];

        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();
builder.Services.AddTraderApiClient(sp => new Uri(sp.GetRequiredService<NavigationManager>().BaseUri));
builder.Services.AddScoped<IClientStore, ProtectedBrowserClientStore>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TraderDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/hubs"),
    pages => pages.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapMarketEndpoints();
app.MapNewsEndpoints();
app.MapSuggestionEndpoints();
app.MapBacktestEndpoints();
app.MapJournalEndpoints();
app.MapAlertEndpoints();
app.MapSettingsEndpoints();
app.MapHub<NewsHub>("/hubs/news").RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(Trader.Shared._Imports).Assembly);

app.Run();

public partial class Program;
