using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface IMarketDataProvider
{
    Task<ChartSeries> GetChartAsync(string symbol, string interval, string range, CancellationToken cancellationToken);
}
