using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface IUserPreferences
{
    Task<UserPreferences> GetAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveAsync(string userId, UserPreferences preferences, CancellationToken cancellationToken);
}
