using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trader.Core.Journal;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

public class PositionCalculatorTests
{
    private static PositionInput Input(
        double entry = 100,
        double stop = 98,
        double? target = null,
        double capital = 10_000,
        double risk = 1,
        double leverage = 5,
        double fx = 1,
        AssetClass assetClass = AssetClass.Stock) =>
        new(capital, risk, entry, stop, target, leverage, fx, assetClass);

    [Fact]
    public void Calculate_Long_SizesThePositionFromRiskAndStopDistance()
    {
        PositionResult result = PositionCalculator.Calculate(Input());

        result.IsValid.Should().BeTrue();
        result.Direction.Should().Be(TradeDirection.Buy);
        result.RiskAmount.Should().Be(100);
        result.StopDistance.Should().Be(2);
        result.Units.Should().BeApproximately(50, 1e-9);
        result.Notional.Should().BeApproximately(5_000, 1e-9);
        result.MarginRequired.Should().BeApproximately(1_000, 1e-9);
        result.RiskReward.Should().BeNull();
    }

    [Fact]
    public void Calculate_Short_FloorsToWholeUnitsAndReportsActualRisk()
    {
        PositionResult result = PositionCalculator.Calculate(Input(entry: 100, stop: 103));

        result.Direction.Should().Be(TradeDirection.Sell);
        result.Units.Should().BeApproximately(100 / 3.0, 1e-9);
        result.WholeUnits.Should().Be(33);
        result.ActualRiskAmount.Should().BeApproximately(99, 1e-9);
    }

    [Fact]
    public void Calculate_WithTarget_ComputesRewardAndRiskReward()
    {
        PositionResult result = PositionCalculator.Calculate(Input(target: 106));

        result.RewardAmount.Should().BeApproximately(300, 1e-9);
        result.RiskReward.Should().BeApproximately(3, 1e-9);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_TargetOnWrongSide_WarnsAndIgnoresReward()
    {
        PositionResult result = PositionCalculator.Calculate(Input(target: 95));

        result.RiskReward.Should().BeNull();
        result.Warnings.Should().ContainSingle(w => w.Contains("lado errado"));
    }

    [Fact]
    public void Calculate_Forex_ReportsStandardLots()
    {
        PositionResult result = PositionCalculator.Calculate(Input(entry: 1.10, stop: 1.095, assetClass: AssetClass.Forex, leverage: 30));

        result.Units.Should().BeApproximately(20_000, 1e-6);
        result.Lots.Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void Calculate_FxRate_ScalesUnitsInverselyToRiskPerUnit()
    {
        PositionResult result = PositionCalculator.Calculate(Input(fx: 0.9));

        result.Units.Should().BeApproximately(100 / 1.8, 1e-9);
    }

    [Fact]
    public void Calculate_HighRisk_Warns()
    {
        PositionCalculator.Calculate(Input(risk: 3)).Warnings.Should().Contain(w => w.Contains("2%"));
    }

    [Fact]
    public void Calculate_LowRiskReward_Warns()
    {
        PositionCalculator.Calculate(Input(target: 101)).Warnings.Should().Contain(w => w.Contains("R:R"));
    }

    [Fact]
    public void Calculate_MarginAboveCapital_Warns()
    {
        PositionResult result = PositionCalculator.Calculate(Input(entry: 1000, stop: 999));

        result.MarginRequired.Should().BeGreaterThan(10_000);
        result.Warnings.Should().Contain(w => w.Contains("margem"));
    }

    [Theory]
    [InlineData(0, 1, 100, 98)]
    [InlineData(10_000, 0, 100, 98)]
    [InlineData(10_000, 1, 100, 100)]
    [InlineData(10_000, 1, 0, 98)]
    public void Calculate_InvalidInput_IsRejected(double capital, double risk, double entry, double stop)
    {
        PositionResult result = PositionCalculator.Calculate(Input(entry, stop, capital: capital, risk: risk));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }
}

public class JournalCalculatorTests
{
    private static JournalTrade Closed(TradeDirection direction, double entry, double exit, double qty, double? stop, double fees = 0, double fx = 1) => new()
    {
        Direction = direction,
        EntryPrice = entry,
        ExitPrice = exit,
        Quantity = qty,
        StopLoss = stop,
        Fees = fees,
        FxRate = fx,
        Status = TradeStatus.Closed
    };

    [Fact]
    public void Compute_ClosedLong_ReturnsNetPnlAndR()
    {
        TradeOutcome outcome = JournalCalculator.Compute(Closed(TradeDirection.Buy, 100, 104, 10, 98, fees: 2));

        outcome.RiskAmount.Should().Be(20);
        outcome.NetPnl.Should().Be(38);
        outcome.RMultiple.Should().BeApproximately(1.9, 1e-9);
    }

    [Fact]
    public void Compute_ClosedShort_ProfitsWhenPriceFalls()
    {
        TradeOutcome outcome = JournalCalculator.Compute(Closed(TradeDirection.Sell, 100, 96, 10, 102));

        outcome.NetPnl.Should().Be(40);
        outcome.RMultiple.Should().BeApproximately(2, 1e-9);
    }

    [Fact]
    public void Compute_FxRate_ConvertsPnlAndRisk()
    {
        TradeOutcome outcome = JournalCalculator.Compute(Closed(TradeDirection.Buy, 100, 104, 10, 98, fx: 0.5));

        outcome.NetPnl.Should().Be(20);
        outcome.RiskAmount.Should().Be(10);
        outcome.RMultiple.Should().BeApproximately(2, 1e-9);
    }

    [Fact]
    public void Compute_WithoutStop_HasNoR()
    {
        TradeOutcome outcome = JournalCalculator.Compute(Closed(TradeDirection.Buy, 100, 104, 10, null));

        outcome.NetPnl.Should().Be(40);
        outcome.RMultiple.Should().BeNull();
    }

    [Fact]
    public void Compute_OpenTrade_HasNoPnl()
    {
        JournalTrade open = new() { Direction = TradeDirection.Buy, EntryPrice = 100, Quantity = 1, StopLoss = 98, Status = TradeStatus.Open };

        JournalCalculator.Compute(open).NetPnl.Should().BeNull();
    }
}

public class JournalValidatorTests
{
    private static OpenTradeRequest Request(TradeDirection direction = TradeDirection.Buy, double entry = 100, double? stop = 98, double? target = 105) =>
        new("AAPL", direction, DateTimeOffset.UtcNow.AddHours(-1), entry, 10, stop, target, 1, 0, null, null, null);

    [Fact]
    public void ValidateOpen_ValidTrade_HasNoErrors() => JournalValidator.ValidateOpen(Request()).Should().BeEmpty();

    [Fact]
    public void ValidateOpen_StopOnProfitSide_IsRejected() =>
        JournalValidator.ValidateOpen(Request(stop: 102)).Should().Contain(e => e.Contains("stop loss"));

    [Fact]
    public void ValidateOpen_ShortWithTargetAbove_IsRejected() =>
        JournalValidator.ValidateOpen(Request(TradeDirection.Sell, stop: 102, target: 105)).Should().Contain(e => e.Contains("take profit"));

    [Fact]
    public void ValidateOpen_FutureEntry_IsRejected() =>
        JournalValidator.ValidateOpen(Request() with { EntryTime = DateTimeOffset.UtcNow.AddDays(1) }).Should().Contain(e => e.Contains("futuro"));

    [Fact]
    public void ValidateClose_ExitBeforeEntry_IsRejected()
    {
        JournalTrade trade = new() { EntryTime = DateTimeOffset.UtcNow.AddHours(-2), Status = TradeStatus.Open };

        JournalValidator.ValidateClose(trade, new CloseTradeRequest(trade.EntryTime.AddHours(-1), 100, 0, null))
            .Should().Contain(e => e.Contains("anterior"));
    }

    [Fact]
    public void ValidateClose_AlreadyClosed_IsRejected()
    {
        JournalTrade trade = new() { EntryTime = DateTimeOffset.UtcNow.AddHours(-2), Status = TradeStatus.Closed };

        JournalValidator.ValidateClose(trade, new CloseTradeRequest(DateTimeOffset.UtcNow, 100, 0, null))
            .Should().Contain(e => e.Contains("já está fechada"));
    }
}

public class JournalStatisticsTests
{
    private static JournalTradeDto Trade(long id, double pnl, double? r, int exitDay, SuggestionStatus? suggestion = null, double? suggestionR = null) => new(
        id, "AAPL", "Apple", TradeDirection.Buy, DateTimeOffset.UnixEpoch, 100, 1, 98, null, 1, 0, string.Empty, string.Empty, null,
        TradeStatus.Closed, DateTimeOffset.UnixEpoch.AddDays(exitDay), 101, 2, pnl, r, suggestion, suggestionR);

    [Fact]
    public void Calculate_MixedResults_ComputesKeyFigures()
    {
        JournalStats stats = JournalStatistics.Calculate(
        [
            Trade(1, 40, 2, 1),
            Trade(2, -20, -1, 2),
            Trade(3, -20, -1, 3),
            Trade(4, 30, 1.5, 4)
        ]);

        stats.ClosedCount.Should().Be(4);
        stats.WinRate.Should().Be(0.5);
        stats.NetPnl.Should().Be(30);
        stats.Expectancy.Should().Be(7.5);
        stats.TotalR.Should().BeApproximately(1.5, 1e-9);
        stats.AverageR.Should().BeApproximately(0.375, 1e-9);
        stats.ProfitFactor.Should().BeApproximately(70 / 40.0, 1e-9);
        stats.MaxDrawdown.Should().Be(40);
        stats.BestTrade.Should().Be(40);
        stats.WorstTrade.Should().Be(-20);
        stats.Curve.Select(c => c.CumulativeR).Should().Equal(40, 20, 0, 30);
    }

    [Fact]
    public void Calculate_LinkedTrades_ComparesYourRWithTheSuggestion()
    {
        JournalStats stats = JournalStatistics.Calculate(
        [
            Trade(1, 40, 2, 1, SuggestionStatus.HitTakeProfit, 2.5),
            Trade(2, -20, -1, 2, SuggestionStatus.HitStopLoss, -1),
            Trade(3, 10, 0.5, 3)
        ]);

        stats.LinkedCount.Should().Be(2);
        stats.LinkedAverageYourR.Should().BeApproximately(0.5, 1e-9);
        stats.LinkedAverageSuggestionR.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void Calculate_NoClosedTrades_ReturnsEmptyStats()
    {
        JournalStats stats = JournalStatistics.Calculate([]);

        stats.ClosedCount.Should().Be(0);
        stats.ProfitFactor.Should().BeNull();
        stats.Curve.Should().BeEmpty();
    }
}

public class JournalIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(TraderFactory factory, string email, HttpClient? creator = null)
    {
        HttpClient client = factory.CreateClient();
        HttpClient registrar = creator ?? client;
        (await registrar.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();

        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static object OpenRequest(long? suggestionId = null) => new
    {
        symbol = "aapl",
        direction = 0,
        entryTime = DateTimeOffset.UtcNow.AddDays(-2),
        entryPrice = 100,
        quantity = 10,
        stopLoss = 98,
        takeProfit = 105,
        fxRate = 1,
        fees = 1,
        platform = "XTB",
        notes = "teste",
        suggestionId
    };

    [Fact]
    public async Task Journal_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        foreach (string path in new[] { "/api/journal/trades", "/api/journal/stats", "/api/journal/account" })
        {
            (await factory.CreateClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Trade_OpenCloseAndStats_FollowTheFullFlow()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "owner@example.com");

        HttpResponseMessage opened = await client.PostAsJsonAsync("/api/journal/trades", OpenRequest());
        opened.StatusCode.Should().Be(HttpStatusCode.OK);
        JournalTradeDto trade = (await opened.Content.ReadFromJsonAsync<JournalTradeDto>())!;
        trade.Symbol.Should().Be("AAPL");
        trade.Name.Should().Be("Apple");
        trade.Status.Should().Be(TradeStatus.Open);

        HttpResponseMessage closed = await client.PostAsJsonAsync(
            $"/api/journal/trades/{trade.Id}/close",
            new { exitTime = DateTimeOffset.UtcNow.AddDays(-1), exitPrice = 104, additionalFees = 1, notes = "fechada" });
        closed.StatusCode.Should().Be(HttpStatusCode.OK);
        JournalTradeDto result = (await closed.Content.ReadFromJsonAsync<JournalTradeDto>())!;
        result.NetPnl.Should().BeApproximately(38, 1e-9);
        result.RMultiple.Should().BeApproximately(1.9, 1e-9);
        result.Notes.Should().Be("fechada");

        (await client.PostAsJsonAsync($"/api/journal/trades/{trade.Id}/close", new { exitTime = DateTimeOffset.UtcNow, exitPrice = 104, additionalFees = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JournalStats stats = (await client.GetFromJsonAsync<JournalStats>("/api/journal/stats"))!;
        stats.ClosedCount.Should().Be(1);
        stats.NetPnl.Should().BeApproximately(38, 1e-9);
    }

    [Fact]
    public async Task Trade_InvalidStop_ReturnsBadRequest()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "owner@example.com");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/journal/trades", new
        {
            symbol = "AAPL", direction = 0, entryTime = DateTimeOffset.UtcNow.AddDays(-1), entryPrice = 100, quantity = 1, stopLoss = 105, fxRate = 1, fees = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Trades_AreIsolatedBetweenUsers()
    {
        await using TraderFactory factory = new();
        HttpClient owner = await SignIn(factory, "owner@example.com");
        HttpClient other = await SignIn(factory, "other@example.com", creator: owner);

        JournalTradeDto trade = (await (await owner.PostAsJsonAsync("/api/journal/trades", OpenRequest())).Content.ReadFromJsonAsync<JournalTradeDto>())!;

        (await other.GetFromJsonAsync<List<JournalTradeDto>>("/api/journal/trades"))!.Should().BeEmpty();
        (await other.DeleteAsync($"/api/journal/trades/{trade.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"/api/journal/trades/{trade.Id}/close", new { exitTime = DateTimeOffset.UtcNow, exitPrice = 101, additionalFees = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<List<JournalTradeDto>>("/api/journal/trades"))!.Should().ContainSingle();
        (await owner.DeleteAsync($"/api/journal/trades/{trade.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Account_IsPerUser_AndValidated()
    {
        await using TraderFactory factory = new();
        HttpClient owner = await SignIn(factory, "owner@example.com");
        HttpClient other = await SignIn(factory, "other@example.com", creator: owner);

        (await owner.PutAsJsonAsync("/api/journal/account", new AccountProfile(25_000, 0.5, "usd"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.PutAsJsonAsync("/api/journal/account", new AccountProfile(-1, 0.5, "EUR"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        AccountProfile mine = (await owner.GetFromJsonAsync<AccountProfile>("/api/journal/account"))!;
        AccountProfile theirs = (await other.GetFromJsonAsync<AccountProfile>("/api/journal/account"))!;

        mine.Should().Be(new AccountProfile(25_000, 0.5, "USD"));
        theirs.Should().Be(new AccountProfile(10_000, 1, "EUR"));
    }

    [Fact]
    public async Task Trade_LinkedToClosedSuggestion_AppearsInTheComparison()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "owner@example.com");

        long suggestionId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            TraderDbContext db = scope.ServiceProvider.GetRequiredService<TraderDbContext>();
            Suggestion suggestion = new()
            {
                Symbol = "AAPL", Name = "Apple", Direction = TradeDirection.Buy, Entry = 100, StopLoss = 98, TakeProfit = 105, RiskReward = 2.5,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), ExpiresAt = DateTimeOffset.UtcNow.AddDays(2),
                Status = SuggestionStatus.HitTakeProfit, ClosedAt = DateTimeOffset.UtcNow.AddDays(-1), ExitPrice = 105, RMultiple = 2.5
            };
            db.Suggestions.Add(suggestion);
            await db.SaveChangesAsync();
            suggestionId = suggestion.Id;
        }

        JournalTradeDto trade = (await (await client.PostAsJsonAsync("/api/journal/trades", OpenRequest(suggestionId))).Content.ReadFromJsonAsync<JournalTradeDto>())!;
        await client.PostAsJsonAsync($"/api/journal/trades/{trade.Id}/close", new { exitTime = DateTimeOffset.UtcNow.AddDays(-1), exitPrice = 104, additionalFees = 1 });

        JournalStats stats = (await client.GetFromJsonAsync<JournalStats>("/api/journal/stats"))!;

        stats.LinkedCount.Should().Be(1);
        stats.LinkedAverageSuggestionR.Should().BeApproximately(2.5, 1e-9);
        stats.LinkedAverageYourR.Should().BeApproximately(1.9, 1e-9);
    }

    [Fact]
    public async Task Trade_LinkedToMissingSuggestion_IsRejected()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "owner@example.com");

        (await client.PostAsJsonAsync("/api/journal/trades", OpenRequest(suggestionId: 9999))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
