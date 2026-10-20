namespace Trader.Core.Maintenance;

public sealed record BackupInfo(string Name, long SizeBytes, DateTimeOffset CreatedAt);

public static class BackupSchedule
{
    public static bool IsDue(DateTimeOffset? lastBackup, DateTimeOffset now, TimeSpan interval) =>
        lastBackup is null || now - lastBackup.Value >= interval;
}
