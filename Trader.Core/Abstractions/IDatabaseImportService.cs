namespace Trader.Core.Abstractions;

public interface IDatabaseImportService
{
    bool HasPending { get; }

    Task<string?> StageAsync(Stream content, CancellationToken cancellationToken);

    void CancelPending();
}
