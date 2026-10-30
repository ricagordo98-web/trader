using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface ICommunityService
{
    Task<IReadOnlyList<CommunityTrend>> GetTrendingAsync(CancellationToken cancellationToken);
}
