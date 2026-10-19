namespace Trader.Shared.Services;

public interface IClientStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    Task RemoveAsync(string key);
}

public interface IServerEndpointSettings
{
    bool IsConfigurable { get; }

    string? Url { get; set; }
}

public sealed class FixedServerEndpointSettings : IServerEndpointSettings
{
    public bool IsConfigurable => false;

    public string? Url { get; set; }
}
