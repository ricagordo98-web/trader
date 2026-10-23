using Trader.Core.Models;

namespace Trader.Shared.Services;

public sealed class SymbolDirectory(TraderApiClient api)
{
    private Dictionary<string, WatchlistItem>? _items;
    private Task? _loading;

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    public WatchlistItem? Find(string symbol) => _items is not null && _items.TryGetValue(symbol, out WatchlistItem? item) ? item : null;

    public string Describe(string symbol, string? fallbackName = null) =>
        Find(symbol) is { } item ? $"{item.Name} · {ClassLabel(item.AssetClass)}" : fallbackName ?? symbol;

    public static string ClassLabel(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Stock => "Ação",
        AssetClass.Etf => "ETF",
        AssetClass.Commodity => "Mercadoria",
        AssetClass.Forex => "Forex",
        AssetClass.Crypto => "Cripto",
        AssetClass.Index => "Índice",
        _ => string.Empty
    };

    private async Task LoadAsync()
    {
        try
        {
            _items = (await api.GetWatchlistAsync()).GroupBy(i => i.Symbol).ToDictionary(g => g.Key, g => g.First());
        }
        catch (HttpRequestException)
        {
            _items = [];
            _loading = null;
        }
    }
}
