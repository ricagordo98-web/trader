using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface IMarketDataService
{
    Task<IReadOnlyList<InstrumentQuote>> GetQuotesAsync(AssetClass? assetClass, CancellationToken cancellationToken);

    Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, string range, CancellationToken cancellationToken);

    Task<double?> GetLatestPriceAsync(string symbol, CancellationToken cancellationToken);

    Task<IReadOnlyList<Candle>> GetLiveCandlesAsync(string symbol, string interval, string range, CancellationToken cancellationToken);
}
