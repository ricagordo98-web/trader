using Trader.Core.Intraday;

namespace Trader.Core.Abstractions;

public interface IIntradaySettings
{
    Task<IntradayOptions> GetAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveAsync(IntradayOptions options, CancellationToken cancellationToken);

    Task ResetAsync(CancellationToken cancellationToken);

    Task LoadAsync(CancellationToken cancellationToken);
}
