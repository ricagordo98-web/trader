using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface ISourceConnector
{
    string Name { get; }

    TimeSpan Interval { get; }

    bool IsEnabled { get; }

    string? DisabledReason => null;

    Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken);
}
