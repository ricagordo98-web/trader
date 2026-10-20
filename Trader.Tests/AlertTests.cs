using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Models;

namespace Trader.Tests;

public class AlertFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

    private static Suggestion Make(SuggestionStatus status, TradeDirection direction = TradeDirection.Buy) => new()
    {
        Id = 7,
        Symbol = "AAPL",
        Direction = direction,
        Confidence = 0.72,
        Entry = 100,
        StopLoss = 98,
        TakeProfit = 105,
        Status = status,
        ExitPrice = 105,
        RMultiple = 2.5
    };

    [Fact]
    public void ForNewSuggestion_Buy_DescribesLevelsAndConfidence()
    {
        Alert alert = AlertFactory.ForNewSuggestion(Make(SuggestionStatus.Open), Now);

        alert.Kind.Should().Be(AlertKind.NewSuggestion);
        alert.Title.Should().Be("Nova sugestão: COMPRA AAPL");
        alert.Message.Should().Contain("72%").And.Contain("SL 98").And.Contain("TP 105");
        alert.SuggestionId.Should().Be(7);
    }

    [Fact]
    public void ForNewSuggestion_Sell_UsesSellLabel() =>
        AlertFactory.ForNewSuggestion(Make(SuggestionStatus.Open, TradeDirection.Sell), Now).Title.Should().Contain("VENDA");

    [Theory]
    [InlineData(SuggestionStatus.HitTakeProfit, AlertKind.TakeProfit)]
    [InlineData(SuggestionStatus.HitStopLoss, AlertKind.StopLoss)]
    [InlineData(SuggestionStatus.Expired, AlertKind.Expired)]
    public void ForOutcome_ClosedSuggestion_MapsKind(SuggestionStatus status, AlertKind kind)
    {
        Alert? alert = AlertFactory.ForOutcome(Make(status), Now);

        alert.Should().NotBeNull();
        alert!.Kind.Should().Be(kind);
        alert.Message.Should().Contain("+2.50 R");
    }

    [Fact]
    public void ForOutcome_OpenSuggestion_ReturnsNull() =>
        AlertFactory.ForOutcome(Make(SuggestionStatus.Open), Now).Should().BeNull();
}

public class AlertIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(TraderFactory factory, string email, HttpClient? creator = null)
    {
        HttpClient client = factory.CreateClient();
        (await (creator ?? client).PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();

        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task Raise(TraderFactory factory, params string[] symbols)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IAlertService service = scope.ServiceProvider.GetRequiredService<IAlertService>();

        await service.RaiseAsync(
            symbols.Select(s => new Alert { Kind = AlertKind.NewSuggestion, Symbol = s, Title = $"Nova {s}", Message = "m", SuggestionId = 1, CreatedAt = DateTimeOffset.UtcNow }).ToList(),
            CancellationToken.None);
    }

    [Fact]
    public async Task Alerts_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().GetAsync("/api/alerts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateClient().PostAsync("/api/alerts/seen", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Feed_AfterRaise_CountsUnreadUntilMarkedSeen()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "a@example.com");
        await Raise(factory, "AAPL", "MSFT");

        AlertFeed before = (await client.GetFromJsonAsync<AlertFeed>("/api/alerts"))!;
        before.UnreadCount.Should().Be(2);
        before.Items.Should().OnlyContain(a => a.Unread);

        (await client.PostAsync("/api/alerts/seen", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        AlertFeed after = (await client.GetFromJsonAsync<AlertFeed>("/api/alerts"))!;
        after.UnreadCount.Should().Be(0);
        after.Items.Should().HaveCount(2).And.OnlyContain(a => !a.Unread);
    }

    [Fact]
    public async Task Seen_IsTrackedPerUser()
    {
        await using TraderFactory factory = new();
        HttpClient first = await SignIn(factory, "a@example.com");
        HttpClient second = await SignIn(factory, "b@example.com", first);
        await Raise(factory, "AAPL");

        await first.PostAsync("/api/alerts/seen", null);

        (await first.GetFromJsonAsync<AlertFeed>("/api/alerts"))!.UnreadCount.Should().Be(0);
        (await second.GetFromJsonAsync<AlertFeed>("/api/alerts"))!.UnreadCount.Should().Be(1);
    }
}
