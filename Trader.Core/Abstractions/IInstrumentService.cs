using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface IInstrumentService
{
    Task<IReadOnlyList<Instrument>> ListAsync(CancellationToken cancellationToken);

    Task<AddInstrumentResult> AddAsync(AddInstrumentRequest request, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string symbol, CancellationToken cancellationToken);
}
