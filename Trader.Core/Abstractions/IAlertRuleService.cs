using Trader.Core.Alerts;
using Trader.Core.Intraday;

namespace Trader.Core.Abstractions;

public interface IAlertRuleService
{
    Task<IReadOnlyList<AlertRuleDto>> ListAsync(string userId, CancellationToken cancellationToken);

    Task<(AlertRuleDto? Rule, IReadOnlyList<string> Errors)> SaveAsync(string userId, AlertRuleDto rule, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string userId, long id, CancellationToken cancellationToken);

    Task<int> EvaluateAsync(IReadOnlyList<RadarRow> board, IReadOnlyList<RadarEvent> events, DateTimeOffset now, CancellationToken cancellationToken);
}
