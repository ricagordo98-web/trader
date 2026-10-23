using Microsoft.Extensions.Logging;
using Trader.Shared.Extensions;
using Trader.Shared.Services;

namespace Trader;

public static class MauiProgram
{
    public const string ServerUrlKey = "ServerUrl";

    internal static string DefaultServerUrl =>
        DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5256" : "http://localhost:5256";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<IServerEndpointSettings, PreferencesServerEndpointSettings>();
        builder.Services.AddScoped<IClientStore, SecureClientStore>();
        builder.Services.AddTraderApiClient(_ => new Uri(Preferences.Default.Get(ServerUrlKey, DefaultServerUrl)));
        builder.Services.AddTraderShared();
        builder.Logging.AddProvider(new ErrorFileLoggerProvider());

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
