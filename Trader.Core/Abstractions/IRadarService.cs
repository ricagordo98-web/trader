using Trader.Core.Intraday;

namespace Trader.Core.Abstractions;

public interface IRadarService
{
    RadarSnapshot GetSnapshot();

    Task ScanAsync(CancellationToken cancellationToken);
}
