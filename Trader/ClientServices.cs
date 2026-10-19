using Trader.Shared.Services;

namespace Trader;

public sealed class SecureClientStore : IClientStore
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SecureStorage.Default.RemoveAll();
            return null;
        }
    }

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public Task RemoveAsync(string key)
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}

public sealed class PreferencesServerEndpointSettings : IServerEndpointSettings
{
    public bool IsConfigurable => true;

    public string? Url
    {
        get => Preferences.Default.Get(MauiProgram.ServerUrlKey, MauiProgram.DefaultServerUrl);
        set => Preferences.Default.Set(MauiProgram.ServerUrlKey, value ?? MauiProgram.DefaultServerUrl);
    }
}
