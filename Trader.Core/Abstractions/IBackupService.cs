using Trader.Core.Maintenance;

namespace Trader.Core.Abstractions;

public interface IBackupService
{
    Task<BackupInfo> CreateAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken);
}
