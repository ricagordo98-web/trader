using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Trader.Core.Alerts;
using Trader.Core.Journal;
using Trader.Core.Models;

namespace Trader.Tests;

public class LevelCheckerTests
{
    private static JournalTrade Trade(TradeDirection direction, double? stop, double? target) => new()
    {
        Direction = direction,
        Status = TradeStatus.Open,
        EntryPrice = 100,
        StopLoss = stop,
        TakeProfit = target
    };

    [Theory]
    [InlineData(97.9, TradeLevel.StopLoss)]
    [InlineData(98.0, TradeLevel.StopLoss)]
    [InlineData(105.0, TradeLevel.TakeProfit)]
    [InlineData(110.0, TradeLevel.TakeProfit)]
    public void Check_BuyHittingLevels_ReturnsLevel(double price, TradeLevel expected) =>
        LevelChecker.Check(Trade(TradeDirection.Buy, 98, 105), price).Should().Be(expected);

    [Fact]
    public void Check_BuyBetweenLevels_ReturnsNull() =>
        LevelChecker.Check(Trade(TradeDirection.Buy, 98, 105), 101).Should().BeNull();

    [Theory]
    [InlineData(102.0, TradeLevel.StopLoss)]
    [InlineData(95.0, TradeLevel.TakeProfit)]
    public void Check_SellHittingLevels_ReturnsLevel(double price, TradeLevel expected) =>
        LevelChecker.Check(Trade(TradeDirection.Sell, 102, 95), price).Should().Be(expected);

    [Fact]
    public void Check_AlreadyAlerted_ReturnsNull()
    {
        JournalTrade trade = Trade(TradeDirection.Buy, 98, 105);
        trade.StopLossAlerted = true;

        LevelChecker.Check(trade, 90).Should().BeNull();
    }

    [Fact]
    public void Check_ClosedTrade_ReturnsNull()
    {
        JournalTrade trade = Trade(TradeDirection.Buy, 98, 105);
        trade.Status = TradeStatus.Closed;

        LevelChecker.Check(trade, 90).Should().BeNull();
    }

    [Theory]
    [InlineData(PriceCondition.Above, 150, 150, true)]
    [InlineData(PriceCondition.Above, 150, 149.9, false)]
    [InlineData(PriceCondition.Below, 150, 150, true)]
    [InlineData(PriceCondition.Below, 150, 151, false)]
    public void IsTriggered_Condition_ComparesPrice(PriceCondition condition, double target, double price, bool expected) =>
        LevelChecker.IsTriggered(new PriceAlert { Condition = condition, Price = target }, price).Should().Be(expected);

    [Fact]
    public void IsTriggered_AlreadyTriggered_ReturnsFalse() =>
        LevelChecker.IsTriggered(new PriceAlert { Condition = PriceCondition.Above, Price = 1, TriggeredAt = DateTimeOffset.UtcNow }, 5).Should().BeFalse();

    [Theory]
    [InlineData("AAPL", 150, true)]
    [InlineData("", 150, false)]
    [InlineData("AAPL", 0, false)]
    [InlineData("AAPL", double.NaN, false)]
    public void Validate_Request_FlagsInvalidInput(string symbol, double price, bool valid) =>
        (PriceAlertValidator.Validate(new CreatePriceAlertRequest(symbol, PriceCondition.Above, price, null)).Count == 0).Should().Be(valid);

    [Fact]
    public void ForTradeLevel_StopLoss_TargetsTheTradeOwner()
    {
        JournalTrade trade = Trade(TradeDirection.Buy, 98, 105);
        trade.UserId = "u1";
        trade.Symbol = "AAPL";

        Alert alert = AlertFactory.ForTradeLevel(trade, TradeLevel.StopLoss, 97.5, DateTimeOffset.UtcNow);

        alert.UserId.Should().Be("u1");
        alert.Kind.Should().Be(AlertKind.TradeStopLoss);
        alert.Title.Should().Contain("Stop loss").And.Contain("AAPL");
    }
}

public class PriceAlertIntegrationTests
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

    [Fact]
    public async Task PriceAlerts_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().GetAsync("/api/alerts/price")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PriceAlerts_AddListDelete_WorksPerUser()
    {
        await using TraderFactory factory = new();
        HttpClient first = await SignIn(factory, "a@example.com");
        HttpClient second = await SignIn(factory, "b@example.com", first);

        HttpResponseMessage created = await first.PostAsJsonAsync("/api/alerts/price", new CreatePriceAlertRequest("aapl", PriceCondition.Above, 200, "alvo"));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        PriceAlertDto alert = (await created.Content.ReadFromJsonAsync<PriceAlertDto>())!;
        alert.Symbol.Should().Be("AAPL");

        (await first.GetFromJsonAsync<List<PriceAlertDto>>("/api/alerts/price"))!.Should().ContainSingle();
        (await second.GetFromJsonAsync<List<PriceAlertDto>>("/api/alerts/price"))!.Should().BeEmpty();
        (await second.DeleteAsync($"/api/alerts/price/{alert.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await first.DeleteAsync($"/api/alerts/price/{alert.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PriceAlerts_InvalidPrice_ReturnsBadRequest()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "a@example.com");

        (await client.PostAsJsonAsync("/api/alerts/price", new CreatePriceAlertRequest("AAPL", PriceCondition.Above, 0, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
