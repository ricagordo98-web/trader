namespace Trader.Core.Abstractions;

public interface IChartDrawingService
{
    Task<string> GetAsync(string symbol, CancellationToken cancellationToken);

    Task SaveAsync(string symbol, string json, CancellationToken cancellationToken);
}
