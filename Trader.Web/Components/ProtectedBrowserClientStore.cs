using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Trader.Shared.Services;

namespace Trader.Web.Components;

public sealed class ProtectedBrowserClientStore(ProtectedLocalStorage storage) : IClientStore
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            ProtectedBrowserStorageResult<string> result = await storage.GetAsync<string>(key);
            return result.Success ? result.Value : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string value) => await storage.SetAsync(key, value);

    public async Task RemoveAsync(string key) => await storage.DeleteAsync(key);
}
