using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Maintenance;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Backup;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string Directory { get; set; } = "backups";

    public int IntervalHours { get; set; } = 24;

    public int KeepCount { get; set; } = 14;
}

public sealed class BackupService(TraderDbContext dbContext, IOptions<BackupOptions> options, ILogger<BackupService> logger) : IBackupService
{
    private const string FilePrefix = "trader-";
    private const string FileExtension = ".db";

    private readonly BackupOptions _options = options.Value;

    public async Task<BackupInfo> CreateAsync(CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(_options.Directory);

        string path = Path.GetFullPath(Path.Combine(_options.Directory, $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}{FileExtension}"));
        await dbContext.Database.ExecuteSqlAsync($"VACUUM INTO {path}", cancellationToken);

        logger.LogInformation("Database backup created at {Path}", path);
        Prune();
        return ToInfo(new FileInfo(path));
    }

    public Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(_options.Directory))
        {
            return Task.FromResult<IReadOnlyList<BackupInfo>>([]);
        }

        IReadOnlyList<BackupInfo> backups = new DirectoryInfo(_options.Directory)
            .GetFiles($"{FilePrefix}*{FileExtension}")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(ToInfo)
            .ToList();

        return Task.FromResult(backups);
    }

    private void Prune()
    {
        IEnumerable<FileInfo> old = new DirectoryInfo(_options.Directory)
            .GetFiles($"{FilePrefix}*{FileExtension}")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(Math.Max(1, _options.KeepCount));

        foreach (FileInfo file in old)
        {
            file.Delete();
        }
    }

    private static BackupInfo ToInfo(FileInfo file) => new(file.Name, file.Length, new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero));
}
