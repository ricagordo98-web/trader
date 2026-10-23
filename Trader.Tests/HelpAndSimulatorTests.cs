using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Help;
using Trader.Core.Models;
using Trader.Core.Simulator;
using Trader.Infrastructure.Persistence;
using Trader.Infrastructure.Simulator;

namespace Trader.Tests;

public class GlossaryTests
{
    [Fact]
    public void Entries_HaveUniqueKeysAndCompleteTexts()
    {
        Glossary.Entries.Select(e => e.Key).Should().OnlyHaveUniqueItems();
        Glossary.Entries.Should().OnlyContain(e => e.Term.Length > 0 && e.Short.Length > 10 && e.Long.Length > 20 && e.Where.Length > 0);
        Glossary.Entries.Count.Should().BeGreaterThan(30);
    }

    [Fact]
    public void Search_MatchesTermShortAndLongText()
    {
        Glossary.Search("vwap").Should().Contain(e => e.Key == "vwap");
        Glossary.Search("DRAWDOWN").Should().Contain(e => e.Key == "drawdown");
        Glossary.Search("zzzz-nope").Should().BeEmpty();
        Glossary.Search("  ").Should().HaveCount(Glossary.Entries.Count);
    }

    [Theory]
    [InlineData("vwap", "vwap")]
    [InlineData("relative-volume", "rvol")]
    [InlineData("catalyst", "catalisador")]
    [InlineData("trend", "ema")]
    public void KeyForSignal_PointsToAnExistingEntry(string signal, string key)
    {
        Glossary.KeyForSignal(signal).Should().Be(key);
        Glossary.Find(key).Should().NotBeNull();
    }

    [Fact]
    public void KeyForSignal_UnknownSignal_ReturnsNull() => Glossary.KeyForSignal("whatever").Should().BeNull();

    [Fact]
    public void Tour_LinksOnlyToExistingRoutes() =>
        HelpContent.Tour.Where(s => s.Link is not null).Select(s => s.Link).Should().OnlyContain(l => new[] { "/", "/trading", "/charts", "/prediction", "/journal", "/help" }.Contains(l));
}

public class HowToReadTests
{
    private static SuggestionDto Make(TradeDirection direction, SuggestionHorizon horizon) =>
        new(1, "AAPL", "Apple", direction, 0.7, 100, direction == TradeDirection.Buy ? 98 : 102, direction == TradeDirection.Buy ? 104 : 96, 2, "x", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SuggestionStatus.Open, null, null, null, [], [], horizon);

    [Fact]
    public void HowToRead_Buy_ExplainsLevelsAndBreakEvenHitRate()
    {
        IReadOnlyList<ReadingLine> lines = HelpContent.HowToRead(Make(TradeDirection.Buy, SuggestionHorizon.Swing));

        lines.Select(l => l.Title).Should().Contain(["Direção", "Entrada", "Stop loss", "Take profit", "Risco/recompensa", "Certeza", "Horizonte"]);
        lines.Single(l => l.Title == "Stop loss").Text.Should().Contain("2,0%");
        lines.Single(l => l.Title == "Risco/recompensa").Text.Should().Contain("33%");
        lines.Single(l => l.Title == "Direção").Text.Should().Contain("COMPRAR");
        lines.Single(l => l.Title == "Horizonte").Text.Should().Contain("5 dias");
    }

    [Fact]
    public void HowToRead_SellIntraday_ExplainsShortAndHours()
    {
        IReadOnlyList<ReadingLine> lines = HelpContent.HowToRead(Make(TradeDirection.Sell, SuggestionHorizon.Intraday));

        lines.Single(l => l.Title == "Direção").Text.Should().Contain("VENDER");
        lines.Single(l => l.Title == "Horizonte").Text.Should().Contain("3 horas");
    }
}

public class PaperCalculatorTests
{
    private static PaperTrade Trade(TradeDirection direction = TradeDirection.Buy) => new()
    {
        Direction = direction,
        Quantity = 10,
        EntryPrice = 100,
        StopLoss = direction == TradeDirection.Buy ? 95 : 105,
        TakeProfit = direction == TradeDirection.Buy ? 110 : 90,
        OpenedAt = new DateTimeOffset(2026, 1, 5, 15, 0, 0, TimeSpan.Zero)
    };

    private static Candle At(int minutes, double low, double high) =>
        new(Trade().OpenedAt.AddMinutes(minutes), (low + high) / 2, high, low, (low + high) / 2, 100);

    [Fact]
    public void Size_RisksTheConfiguredPercentOfCapital() =>
        PaperCalculator.Size(10_000, 1, 100, 95).Should().Be(20);

    [Theory]
    [InlineData(0, 1, 100, 95)]
    [InlineData(10_000, 0, 100, 95)]
    [InlineData(10_000, 1, 100, 100)]
    public void Size_InvalidInputs_ReturnsZero(double capital, double risk, double entry, double stop) =>
        PaperCalculator.Size(capital, risk, entry, stop).Should().Be(0);

    [Fact]
    public void PnlAndR_BuyAndSell_HaveTheRightSign()
    {
        PaperCalculator.Pnl(Trade(), 105).Should().Be(50);
        PaperCalculator.RMultiple(Trade(), 105).Should().Be(1);
        PaperCalculator.Pnl(Trade(TradeDirection.Sell), 95).Should().Be(50);
        PaperCalculator.RMultiple(Trade(TradeDirection.Sell), 105).Should().Be(-1);
    }

    [Fact]
    public void Resolve_TargetHit_ClosesAtTheTarget()
    {
        PaperOutcome? outcome = PaperCalculator.Resolve(Trade(), [At(5, 99, 103), At(10, 101, 111)]);

        outcome!.Status.Should().Be(PaperStatus.HitTakeProfit);
        outcome.ExitPrice.Should().Be(110);
    }

    [Fact]
    public void Resolve_StopAndTargetInTheSameCandle_AssumesTheStop() =>
        PaperCalculator.Resolve(Trade(), [At(5, 94, 111)])!.Status.Should().Be(PaperStatus.HitStopLoss);

    [Fact]
    public void Resolve_IgnoresCandlesBeforeTheEntryAndReturnsNullWhenNothingHit() =>
        PaperCalculator.Resolve(Trade(), [At(-30, 90, 120), At(5, 98, 103)]).Should().BeNull();

    [Theory]
    [InlineData(TradeDirection.Buy, 100, 99, 101, true)]
    [InlineData(TradeDirection.Buy, 100, 101, 99, false)]
    [InlineData(TradeDirection.Sell, 100, 101, 99, true)]
    [InlineData(TradeDirection.Sell, 100, 99, 101, false)]
    public void Validate_RequiresStopAndTargetOnTheRightSides(TradeDirection direction, double entry, double stop, double target, bool valid) =>
        (PaperCalculator.Validate(new OpenPaperTradeRequest("X", direction, entry, stop, target, null), entry).Count == 0).Should().Be(valid);

    [Fact]
    public void Summarize_CombinesRealizedAndUnrealized()
    {
        PaperTradeDto win = new(1, "A", "A", TradeDirection.Buy, 1, 1, 1, 1, DateTimeOffset.UtcNow, PaperStatus.HitTakeProfit, DateTimeOffset.UtcNow, 2, null, 100, 2);
        PaperTradeDto loss = win with { Id = 2, Status = PaperStatus.HitStopLoss, Pnl = -50, RMultiple = -1 };
        PaperTradeDto open = win with { Id = 3, Status = PaperStatus.Open, Pnl = 25, RMultiple = 0.5 };

        PaperSummary summary = PaperCalculator.Summarize(new PaperAccount(10_000, 1), [open], [win, loss]);

        summary.RealizedPnl.Should().Be(50);
        summary.UnrealizedPnl.Should().Be(25);
        summary.Equity.Should().Be(10_075);
        summary.WinRate.Should().Be(0.5);
        summary.TotalR.Should().Be(1);
    }
}

public class PaperTradingServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IMarketDataService _marketData = Substitute.For<IMarketDataService>();
    private TraderDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
        _dbContext.Instruments.Add(new Instrument { Symbol = "SIMX", Name = "Sim Inc", AssetClass = AssetClass.Stock, Currency = "USD", SortOrder = 9999 });
        await _dbContext.SaveChangesAsync();
        _marketData.GetLatestPriceAsync("SIMX", Arg.Any<CancellationToken>()).Returns(100.0);
        _marketData.GetCandlesAsync("SIMX", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Candle>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private PaperTradingService Service() => new(_dbContext, _marketData, NullLogger<PaperTradingService>.Instance);

    [Fact]
    public async Task Open_UsesTheCurrentPriceAndSizesByRisk()
    {
        (PaperTradeDto? trade, IReadOnlyList<string> errors) = await Service().OpenAsync("u1", new OpenPaperTradeRequest("SIMX", TradeDirection.Buy, null, 95, 110, null), CancellationToken.None);

        errors.Should().BeEmpty();
        trade!.EntryPrice.Should().Be(100);
        trade.Quantity.Should().Be(20);
        (await Service().GetAsync("u1", CancellationToken.None)).Open.Should().ContainSingle();
    }

    [Fact]
    public async Task Open_StopOnTheWrongSide_IsRejected()
    {
        (PaperTradeDto? trade, IReadOnlyList<string> errors) = await Service().OpenAsync("u1", new OpenPaperTradeRequest("SIMX", TradeDirection.Buy, null, 105, 110, null), CancellationToken.None);

        trade.Should().BeNull();
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_WhenALaterCandleHitsTheTarget_ClosesTheTradeWithProfit()
    {
        PaperTradingService service = Service();
        await service.OpenAsync("u1", new OpenPaperTradeRequest("SIMX", TradeDirection.Buy, null, 95, 110, null), CancellationToken.None);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(10);
        _marketData.GetCandlesAsync("SIMX", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([new Candle(later, 100, 111, 99, 110, 10)]);

        PaperOverview overview = await service.GetAsync("u1", CancellationToken.None);

        overview.Open.Should().BeEmpty();
        PaperTradeDto closed = overview.Closed.Should().ContainSingle().Subject;
        closed.Status.Should().Be(PaperStatus.HitTakeProfit);
        closed.Pnl.Should().Be(200);
        closed.RMultiple.Should().Be(2);
    }

    [Fact]
    public async Task Close_ManualClose_UsesTheLatestPrice()
    {
        PaperTradingService service = Service();
        (PaperTradeDto? trade, _) = await service.OpenAsync("u1", new OpenPaperTradeRequest("SIMX", TradeDirection.Buy, null, 95, 110, null), CancellationToken.None);
        _marketData.GetLatestPriceAsync("SIMX", Arg.Any<CancellationToken>()).Returns(103.0);

        (await service.CloseAsync("u1", trade!.Id, CancellationToken.None)).Should().BeTrue();

        PaperTradeDto closed = (await service.GetAsync("u1", CancellationToken.None)).Closed.Single();
        closed.Status.Should().Be(PaperStatus.ClosedManually);
        closed.Pnl.Should().Be(60);
    }

    [Fact]
    public async Task Trades_AreScopedPerUser_AndResetOnlyClearsTheOwner()
    {
        PaperTradingService service = Service();
        await service.OpenAsync("u1", new OpenPaperTradeRequest("SIMX", TradeDirection.Buy, null, 95, 110, null), CancellationToken.None);
        await service.OpenAsync("u2", new OpenPaperTradeRequest("SIMX", TradeDirection.Sell, null, 105, 90, null), CancellationToken.None);

        await service.ResetAsync("u1", CancellationToken.None);

        (await service.GetAsync("u1", CancellationToken.None)).Open.Should().BeEmpty();
        (await service.GetAsync("u2", CancellationToken.None)).Open.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveAccount_OutOfRange_IsRejected()
    {
        (await Service().SaveAccountAsync("u1", new PaperAccount(50, 1), CancellationToken.None)).Should().NotBeEmpty();
        (await Service().SaveAccountAsync("u1", new PaperAccount(5000, 2), CancellationToken.None)).Should().BeEmpty();
        (await Service().GetAsync("u1", CancellationToken.None)).Account.Should().Be(new PaperAccount(5000, 2));
    }
}
