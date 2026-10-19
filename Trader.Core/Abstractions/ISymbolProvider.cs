namespace Trader.Core.Abstractions;

public interface ISymbolProvider
{
    Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken);
}
