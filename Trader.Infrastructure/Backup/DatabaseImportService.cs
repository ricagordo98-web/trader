using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Backup;

public sealed class DatabaseImportService(TraderDbContext dbContext) : IDatabaseImportService
{
    private string PendingPath => DatabaseImport.PendingPath(new SqliteConnectionStringBuilder(dbContext.Database.GetConnectionString()).DataSource);

    public bool HasPending => File.Exists(PendingPath);

    public async Task<string?> StageAsync(Stream content, CancellationToken cancellationToken)
    {
        string pending = PendingPath;
        string temporary = pending + ".tmp";

        try
        {
            await using (FileStream file = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            if (new FileInfo(temporary).Length == 0)
            {
                return "O ficheiro está vazio.";
            }

            string? error = await DatabaseImport.ValidateAsync(temporary, dbContext.Database.GetMigrations().ToHashSet(), cancellationToken);

            if (error is not null)
            {
                return error;
            }

            DatabaseImport.DeleteSidecars(temporary);
            File.Move(temporary, pending, overwrite: true);
            return null;
        }
        finally
        {
            File.Delete(temporary);
            DatabaseImport.DeleteSidecars(temporary);
        }
    }

    public void CancelPending()
    {
        File.Delete(PendingPath);
        DatabaseImport.DeleteSidecars(PendingPath);
    }
}
