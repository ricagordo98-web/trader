using Trader.Core.Intraday;

namespace Trader.Core.Abstractions;

public sealed record VariantSettings(bool Enabled, IntradayOptions Options);

public interface IIntradaySettings
{
    Task<IntradayOptions> GetAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveAsync(IntradayOptions options, CancellationToken cancellationToken);

    Task ResetAsync(CancellationToken cancellationToken);

    Task LoadAsync(CancellationToken cancellationToken);

    Task<VariantSettings> GetVariantBAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveVariantBAsync(VariantSettings settings, CancellationToken cancellationToken);

    Task ResetVariantBAsync(CancellationToken cancellationToken);
}
