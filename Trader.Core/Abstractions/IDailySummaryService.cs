using Trader.Core.Summary;

namespace Trader.Core.Abstractions;

public interface IDailySummaryService
{
    Task<DailySummary> GenerateAsync(CancellationToken cancellationToken);

    Task<DailySummary?> GetLastAsync(CancellationToken cancellationToken);

    Task<DailySummary> GenerateWeeklyAsync(string userId, CancellationToken cancellationToken);
}

public interface ISummaryNarrator
{
    bool IsEnabled { get; }

    string Name { get; }

    Task<string?> NarrateAsync(DailySummary summary, CancellationToken cancellationToken);
}
