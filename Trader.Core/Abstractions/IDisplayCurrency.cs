using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface IDisplayCurrency
{
    Task<string> GetAsync(CancellationToken cancellationToken);

    Task<string?> SetAsync(string currency, CancellationToken cancellationToken);

    Task<DisplayConversion> GetConversionAsync(CancellationToken cancellationToken);
}
