using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface ISourceStatusRegistry
{
    void Report(SourceStatus status);

    IReadOnlyList<SourceStatus> GetAll();
}
