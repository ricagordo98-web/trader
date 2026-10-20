using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface IMarketDataService
{
    Task<IReadOnlyList<InstrumentQuote>> GetQuotesAsync(AssetClass? assetClass, CancellationToken cancellationToken);

    Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, string range, CancellationToken cancellationToken);

    Task<LiveTick?> GetLiveTickAsync(string symbol, CancellationToken cancellationToken);
}
