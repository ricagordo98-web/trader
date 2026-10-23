using Trader.Core.Backtesting;

namespace Trader.Core.Abstractions;

public interface IIntradayBacktestService
{
    Task<IntradayBacktestResult> RunAsync(IntradayBacktestRequest request, CancellationToken cancellationToken);

    Task<IntradayBacktestResult?> GetLastAsync(CancellationToken cancellationToken);
}
