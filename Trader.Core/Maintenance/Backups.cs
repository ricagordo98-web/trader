namespace Trader.Core.Maintenance;

public sealed record BackupInfo(string Name, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record DatabaseImportStatus(bool Pending);

public static class DatabaseImportLimits
{
    public const long MaxBytes = 1L * 1024 * 1024 * 1024;
}

public static class BackupSchedule
{
    public static bool IsDue(DateTimeOffset? lastBackup, DateTimeOffset now, TimeSpan interval) =>
        lastBackup is null || now - lastBackup.Value >= interval;
}
