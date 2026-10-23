using Trader.Core.Simulator;

namespace Trader.Core.Abstractions;

public interface IPaperTradingService
{
    Task<PaperOverview> GetAsync(string userId, CancellationToken cancellationToken);

    Task<(PaperTradeDto? Trade, IReadOnlyList<string> Errors)> OpenAsync(string userId, OpenPaperTradeRequest request, CancellationToken cancellationToken);

    Task<bool> CloseAsync(string userId, long id, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveAccountAsync(string userId, PaperAccount account, CancellationToken cancellationToken);

    Task ResetAsync(string userId, CancellationToken cancellationToken);
}
