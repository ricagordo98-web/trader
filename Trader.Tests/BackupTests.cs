using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Trader.Core.Maintenance;

namespace Trader.Tests;

public class BackupScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsDue_NoPreviousBackup_ReturnsTrue() =>
        BackupSchedule.IsDue(null, Now, TimeSpan.FromHours(24)).Should().BeTrue();

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(48, true)]
    public void IsDue_DependsOnElapsedTime(int hoursAgo, bool expected) =>
        BackupSchedule.IsDue(Now.AddHours(-hoursAgo), Now, TimeSpan.FromHours(24)).Should().Be(expected);
}

public class BackupIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(TraderFactory factory)
    {
        HttpClient client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { email = "a@example.com", password = Password })).EnsureSuccessStatusCode();
        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email = "a@example.com", password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Backups_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().GetAsync("/api/system/backups")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateBackup_ProducesRestorableCopyThatIsListed()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory);

        HttpResponseMessage created = await client.PostAsync("/api/system/backups", null);
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        List<BackupInfo> listed = (await client.GetFromJsonAsync<List<BackupInfo>>("/api/system/backups"))!;
        listed.Should().ContainSingle();
        listed[0].SizeBytes.Should().BeGreaterThan(0);

        string backupDirectory = factory.BackupDirectory;
        string file = Directory.GetFiles(backupDirectory, "trader-*.db").Single();
        await using SqliteConnection connection = new($"Data Source={file};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AspNetUsers";
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(1);
    }
}
