using Trader.Core.Explain;

namespace Trader.Core.Abstractions;

public interface IEvidenceSource
{
    string Name { get; }

    Task<IReadOnlyList<EvidenceItem>> FetchAsync(EvidenceQuery query, CancellationToken cancellationToken);
}

public interface IMoveNarrator
{
    bool IsEnabled { get; }

    string Name { get; }

    Task<NarrationResult> NarrateAsync(MoveAnalysis analysis, IReadOnlyList<MoveCause> causes, CancellationToken cancellationToken);
}

public sealed record ExplainStats(int Total, int Accepted, int Rejected, int Disabled, int Unavailable);

public interface IMoveExplainer
{
    Task<MoveExplanation?> ExplainAsync(MoveRequest request, CancellationToken cancellationToken);

    Task<ExplainStats> GetStatsAsync(CancellationToken cancellationToken);
}
