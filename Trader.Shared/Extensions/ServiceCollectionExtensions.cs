using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;
using Trader.Shared.Services;

namespace Trader.Shared.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTraderShared(this IServiceCollection services)
    {
        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.ShowTransitionDuration = 120;
            config.SnackbarConfiguration.HideTransitionDuration = 0;
            config.SnackbarConfiguration.VisibleStateDuration = 6000;
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 4;
            config.SnackbarConfiguration.PreventDuplicates = true;
        });
        services.AddScoped<ThemeState>();
        services.AddScoped<TraderApiClient>();
        services.AddScoped<SymbolDirectory>();
        services.TryAddScoped<IServerEndpointSettings, FixedServerEndpointSettings>();
        return services;
    }

    public static IServiceCollection AddTraderApiClient(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddress)
    {
        services.AddAuthorizationCore();
        services.AddScoped(sp => new AuthSession(sp.GetRequiredService<IClientStore>(), new HttpClient { BaseAddress = baseAddress(sp) }));
        services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
        services.AddScoped(sp => new HttpClient(new AuthHttpHandler(sp.GetRequiredService<AuthSession>()) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = baseAddress(sp)
        });

        return services;
    }
}
