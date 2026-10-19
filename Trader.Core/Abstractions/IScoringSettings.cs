using Trader.Core.Scoring;

namespace Trader.Core.Abstractions;

public interface IScoringSettings
{
    Task<ScoringOptions> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(ScoringOptions options, CancellationToken cancellationToken);

    Task ResetAsync(CancellationToken cancellationToken);
}
