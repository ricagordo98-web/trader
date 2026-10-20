using Trader.Core.Backtesting;

namespace Trader.Core.Abstractions;

public interface IBacktestService
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken);

    Task<BacktestResult?> GetLastAsync(CancellationToken cancellationToken);
}
