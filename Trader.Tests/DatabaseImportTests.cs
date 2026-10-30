using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Trader.Core.Maintenance;
using Trader.Infrastructure.Backup;

namespace Trader.Tests;

public class DatabaseImportTests : IDisposable
{
    private const string Password = "Str0ng!Passw0rd";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trader-import-{Guid.NewGuid():N}");

    public DatabaseImportTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }

    private string CreateDatabase(string name, string migration, params string[] extraStatements)
    {
        string file = Path.Combine(_directory, name);
        using SqliteConnection connection = new($"Data Source={file};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL);
            CREATE TABLE Articles (Id INTEGER);
            CREATE TABLE AspNetUsers (Id TEXT);
            INSERT INTO __EFMigrationsHistory VALUES ('{migration}');
            {string.Join(';', extraStatements)}
            """;
        command.ExecuteNonQuery();
        return file;
    }

    private static string? ReadMarker(string file)
    {
        using SqliteConnection connection = new($"Data Source={file};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
        return command.ExecuteScalar() as string;
    }

    [Fact]
    public async Task Validate_TraderDatabaseWithKnownMigration_ReturnsNull()
    {
        string file = CreateDatabase("ok.db", "20260101_Initial");

        string? error = await DatabaseImport.ValidateAsync(file, new HashSet<string> { "20260101_Initial" }, CancellationToken.None);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Validate_NewerMigration_ReturnsError()
    {
        string file = CreateDatabase("newer.db", "20270101_Future");

        string? error = await DatabaseImport.ValidateAsync(file, new HashSet<string> { "20260101_Initial" }, CancellationToken.None);

        error.Should().Contain("mais recente");
    }

    [Fact]
    public async Task Validate_NotADatabase_ReturnsError()
    {
        string file = Path.Combine(_directory, "text.db");
        await File.WriteAllTextAsync(file, "this is not a database, just some text that is long enough to be read");

        string? error = await DatabaseImport.ValidateAsync(file, new HashSet<string>(), CancellationToken.None);

        error.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_SqliteWithoutTraderTables_ReturnsError()
    {
        string file = Path.Combine(_directory, "other.db");
        await using (SqliteConnection connection = new($"Data Source={file};Pooling=False"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Other (Id INTEGER)";
            await command.ExecuteNonQueryAsync();
        }

        string? error = await DatabaseImport.ValidateAsync(file, new HashSet<string>(), CancellationToken.None);

        error.Should().Contain("não é uma base de dados do Trader");
    }

    [Fact]
    public void ApplyPending_ReplacesDatabaseAndKeepsBackupOfTheOldOne()
    {
        string current = CreateDatabase("trader.db", "current");
        string incoming = CreateDatabase("incoming.db", "incoming");
        File.Move(incoming, DatabaseImport.PendingPath(current));
        string backups = Path.Combine(_directory, "backups");

        bool applied = DatabaseImport.ApplyPending(current, backups, null);

        applied.Should().BeTrue();
        ReadMarker(current).Should().Be("incoming");
        File.Exists(DatabaseImport.PendingPath(current)).Should().BeFalse();
        ReadMarker(Directory.GetFiles(backups, "before-import-*.db").Single()).Should().Be("current");
    }

    [Fact]
    public void ApplyPending_WithoutPendingFile_DoesNothing()
    {
        string current = CreateDatabase("trader.db", "current");

        DatabaseImport.ApplyPending(current, Path.Combine(_directory, "backups"), null).Should().BeFalse();

        ReadMarker(current).Should().Be("current");
    }

    [Fact]
    public async Task ImportEndpoint_ValidBackup_IsStagedAndCanBeCancelled()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory);
        (await client.PostAsync("/api/system/backups", null)).EnsureSuccessStatusCode();
        byte[] backup = await File.ReadAllBytesAsync(Directory.GetFiles(factory.BackupDirectory, "trader-*.db").Single());

        HttpResponseMessage imported = await client.PostAsync("/api/system/import", Octet(backup));

        imported.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<DatabaseImportStatus>("/api/system/import"))!.Pending.Should().BeTrue();

        (await client.DeleteAsync("/api/system/import")).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<DatabaseImportStatus>("/api/system/import"))!.Pending.Should().BeFalse();
    }

    [Fact]
    public async Task ImportEndpoint_InvalidFile_ReturnsBadRequestAndStagesNothing()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory);

        HttpResponseMessage imported = await client.PostAsync("/api/system/import", Octet("not a database, only text with some length"u8.ToArray()));

        imported.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<DatabaseImportStatus>("/api/system/import"))!.Pending.Should().BeFalse();
    }

    [Fact]
    public async Task ImportEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().PostAsync("/api/system/import", Octet([1, 2, 3]))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static ByteArrayContent Octet(byte[] bytes)
    {
        ByteArrayContent content = new(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(TraderFactory factory)
    {
        HttpClient client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { email = "a@example.com", password = Password })).EnsureSuccessStatusCode();
        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email = "a@example.com", password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }
}
