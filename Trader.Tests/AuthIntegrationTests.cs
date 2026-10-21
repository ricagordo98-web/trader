using Trader.Infrastructure.Alerts;
using Trader.Infrastructure.MarketData;
using Trader.Infrastructure.Backup;
using Trader.Infrastructure.Calendar;
using Trader.Infrastructure.Summary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trader.Core.Scoring;
using Trader.Infrastructure.News;
using Trader.Infrastructure.Suggestions;

namespace Trader.Tests;

public sealed class TraderFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trader-test-{Guid.NewGuid():N}.db");

    public string BackupDirectory { get; } = Path.Combine(Path.GetTempPath(), $"trader-backups-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Trader", $"Data Source={_databasePath}");
        builder.UseSetting("Backup:Directory", BackupDirectory);
        builder.ConfigureServices(services =>
        {
            foreach (ServiceDescriptor descriptor in services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType is { } type
                    && (type == typeof(IngestionWorker) || type == typeof(SuggestionWorker) || type == typeof(PriceWatchWorker) || type == typeof(BackupWorker) || type == typeof(CalendarAlertWorker) || type == typeof(SummaryWorker) || type == typeof(QuoteWarmupWorker)))
                .ToList())
            {
                services.Remove(descriptor);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(BackupDirectory, true);
        }
        catch (IOException)
        {
        }

        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
            }
        }
    }
}

public class AuthIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private sealed record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private static async Task<HttpClient> SignedInClientAsync(TraderFactory factory, string email = "owner@example.com")
    {
        HttpClient client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();

        HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        Tokens tokens = (await login.Content.ReadFromJsonAsync<Tokens>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Status_FreshDatabase_ReportsRegistrationOpen()
    {
        await using TraderFactory factory = new();
        HttpClient client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<Dictionary<string, bool>>("/api/auth/status");

        status!["registrationOpen"].Should().BeTrue();
    }

    [Fact]
    public async Task Register_FirstAccountThenAnonymousSecond_SecondIsForbidden()
    {
        await using TraderFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/auth/register", new { email = "owner@example.com", password = Password });
        HttpResponseMessage second = await client.PostAsJsonAsync("/api/auth/register", new { email = "intruder@example.com", password = Password });
        var status = await client.GetFromJsonAsync<Dictionary<string, bool>>("/api/auth/status");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        status!["registrationOpen"].Should().BeFalse();
    }

    [Fact]
    public async Task Register_AuthenticatedUser_CanCreateAnotherAccount()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignedInClientAsync(factory);

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/auth/register", new { email = "second@example.com", password = Password });

        created.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_WeakPassword_IsRejected()
    {
        await using TraderFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register", new { email = "owner@example.com", password = "short" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/news")]
    [InlineData("/api/instruments")]
    [InlineData("/api/suggestions")]
    [InlineData("/api/suggestions/track-record")]
    [InlineData("/api/watchlist")]
    [InlineData("/api/sources")]
    [InlineData("/api/settings/scoring")]
    public async Task Api_WithoutToken_ReturnsUnauthorized(string path)
    {
        await using TraderFactory factory = new();
        HttpClient client = factory.CreateClient();

        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NewsHub_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();
        HttpClient client = factory.CreateClient();

        (await client.PostAsync("/hubs/news/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();
        await SignedInClientAsync(factory);
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage response = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.com", password = "Wrong!Passw0rd" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_RepeatedFailures_LocksTheAccount()
    {
        await using TraderFactory factory = new();
        await SignedInClientAsync(factory);
        HttpClient anonymous = factory.CreateClient();

        for (int i = 0; i < 5; i++)
        {
            await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.com", password = "Wrong!Passw0rd" });
        }

        HttpResponseMessage correct = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.com", password = Password });

        correct.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await correct.Content.ReadAsStringAsync()).Should().Contain("LockedOut");
    }

    [Fact]
    public async Task Api_WithValidToken_ReturnsSeededWatchlist()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignedInClientAsync(factory);

        var watchlist = await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/watchlist");

        watchlist.Should().NotBeNullOrEmpty();
        watchlist!.Select(i => i["symbol"].ToString()).Should().Contain("AAPL");
    }

    [Fact]
    public async Task Refresh_WithRefreshToken_IssuesNewAccessToken()
    {
        await using TraderFactory factory = new();
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new { email = "owner@example.com", password = Password });
        Tokens first = (await (await client.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.com", password = Password })).Content.ReadFromJsonAsync<Tokens>())!;

        HttpResponseMessage refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first.RefreshToken });

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await refreshed.Content.ReadFromJsonAsync<Tokens>())!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Watchlist_InvalidSymbol_ReturnsBadRequestWithoutCallingProvider()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignedInClientAsync(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/watchlist", new { symbol = "bad symbol!", name = "Bad", assetClass = 0, currency = "USD" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Watchlist_RemoveExisting_RemovesIt()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignedInClientAsync(factory);

        HttpResponseMessage removed = await client.DeleteAsync("/api/watchlist/ARKK");
        var watchlist = await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/watchlist");

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        watchlist!.Select(i => i["symbol"].ToString()).Should().NotContain("ARKK");
        (await client.DeleteAsync("/api/watchlist/ARKK")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ScoringSettings_SaveAndReset_RoundTrips()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignedInClientAsync(factory);
        ScoringOptions defaults = (await client.GetFromJsonAsync<ScoringOptions>("/api/settings/scoring"))!;

        ScoringOptions changed = (await client.GetFromJsonAsync<ScoringOptions>("/api/settings/scoring"))!;
        changed.ConfidenceThreshold = 0.8;
        (await client.PutAsJsonAsync("/api/settings/scoring", changed)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<ScoringOptions>("/api/settings/scoring"))!.ConfidenceThreshold.Should().Be(0.8);

        (await client.DeleteAsync("/api/settings/scoring")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<ScoringOptions>("/api/settings/scoring"))!.ConfidenceThreshold.Should().Be(defaults.ConfidenceThreshold);
    }

    [Fact]
    public async Task ScoringSettings_OutOfRangeValue_IsRejected()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignedInClientAsync(factory);
        ScoringOptions invalid = (await client.GetFromJsonAsync<ScoringOptions>("/api/settings/scoring"))!;
        invalid.ConfidenceThreshold = 0.1;

        (await client.PutAsJsonAsync("/api/settings/scoring", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
