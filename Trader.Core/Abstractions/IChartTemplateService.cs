using Trader.Core.Charts;

namespace Trader.Core.Abstractions;

public interface IChartTemplateService
{
    Task<IReadOnlyList<ChartTemplate>> GetAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveAsync(string userId, IReadOnlyList<ChartTemplate> templates, CancellationToken cancellationToken);
}
