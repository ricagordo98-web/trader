using Trader.Core.Charts;

namespace Trader.Core.Abstractions;

public interface IChartInsights
{
    Task<IReadOnlyList<AutoLevel>> GetLevelsAsync(string symbol, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChartEventDto>> GetEventsAsync(string symbol, string userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ChartLayoutDto(string Name, string Json, DateTimeOffset SavedAt);

public interface IChartLayoutService
{
    Task<IReadOnlyList<ChartLayoutDto>> ListAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveAsync(string userId, string name, string json, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string userId, string name, CancellationToken cancellationToken);
}
