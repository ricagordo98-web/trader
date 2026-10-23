using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Charts;
using Trader.Core.Explain;
using Trader.Core.Journal;
using Trader.Core.Models;
using Trader.Infrastructure.Charts;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

public class AutoLevelTests
{
    private static Candle Daily(int dayOffset, double high, double low, double close) =>
        new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddDays(dayOffset), close, high, low, close, 1000);

    private static List<Candle> History() => Enumerable.Range(0, 12).Select(i => Daily(i, 100 + i, 90 + i, 95 + i)).ToList();

    [Fact]
    public void Compute_WithOnlyDailyData_ReturnsPreviousDayPivotsAndSwingLevels()
    {
        IReadOnlyList<AutoLevel> levels = AutoLevelCalculator.Compute(History(), []);

        levels.Single(l => l.Label == "Máx. dia anterior").Price.Should().Be(110);
        levels.Single(l => l.Label == "Mín. dia anterior").Price.Should().Be(100);
        levels.Single(l => l.Label == "Fecho dia anterior").Price.Should().Be(105);
        levels.Select(l => l.Group).Distinct().Should().BeEquivalentTo([AutoLevelGroups.PreviousDay, AutoLevelGroups.Pivots, AutoLevelGroups.Swing]);
    }

    [Fact]
    public void Compute_Pivots_FollowTheClassicFormula()
    {
        IReadOnlyList<AutoLevel> levels = AutoLevelCalculator.Compute([.. History().Take(10), Daily(10, 120, 100, 110), Daily(11, 125, 105, 118)], []);

        double pivot = (120 + 100 + 110) / 3.0;
        levels.Single(l => l.Label == "Pivot").Price.Should().BeApproximately(pivot, 1e-9);
        levels.Single(l => l.Label == "R1").Price.Should().BeApproximately(2 * pivot - 100, 1e-9);
        levels.Single(l => l.Label == "S1").Price.Should().BeApproximately(2 * pivot - 120, 1e-9);
        levels.Single(l => l.Label == "R2").Price.Should().BeApproximately(pivot + 20, 1e-9);
    }

    [Fact]
    public void Compute_WithATodaySession_ExcludesTodayFromThePreviousDayAndAddsTheOpeningRange()
    {
        DateTime today = new DateTime(2026, 10, 12);
        List<Candle> daily = [.. History(), Daily(11, 200, 150, 160)];
        List<Candle> intraday = IntradayFixtures.Day(today, 100, 20, i => 100 + 0.1 * i, 1000);

        IReadOnlyList<AutoLevel> levels = AutoLevelCalculator.Compute(daily, intraday);

        levels.Should().Contain(l => l.Group == AutoLevelGroups.OpeningRange && l.Label == "Máx. 30 min");
        levels.Should().Contain(l => l.Group == AutoLevelGroups.OpeningRange && l.Label == "Mín. 30 min");
        levels.Single(l => l.Label == "Máx. dia anterior").Price.Should().BeLessThan(200);
    }

    [Fact]
    public void Compute_OpeningRangeNotYetComplete_IsOmitted()
    {
        List<Candle> intraday = IntradayFixtures.Day(new DateTime(2026, 10, 12), 100, 4, _ => 100, 1000);

        AutoLevelCalculator.Compute(History(), intraday).Should().NotContain(l => l.Group == AutoLevelGroups.OpeningRange);
    }

    [Fact]
    public void Compute_NoData_ReturnsNothing() => AutoLevelCalculator.Compute([], []).Should().BeEmpty();
}

public class HeikinAshiTests
{
    [Fact]
    public void Convert_FollowsTheStandardFormulas()
    {
        List<Candle> candles =
        [
            new(DateTimeOffset.UnixEpoch, 10, 12, 9, 11, 100),
            new(DateTimeOffset.UnixEpoch.AddMinutes(5), 11, 14, 10, 13, 100)
        ];

        IReadOnlyList<Candle> result = HeikinAshi.Convert(candles);

        result[0].Close.Should().Be((10 + 12 + 9 + 11) / 4.0);
        result[0].Open.Should().Be((10 + 11) / 2.0);
        result[1].Open.Should().Be((result[0].Open + result[0].Close) / 2);
        result[1].Close.Should().Be((11 + 14 + 10 + 13) / 4.0);
        result.Should().OnlyContain(c => c.High >= Math.Max(c.Open, c.Close) && c.Low <= Math.Min(c.Open, c.Close));
        result.Select(c => c.Volume).Should().Equal(100, 100);
    }
}

public class ChartLayoutServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private TraderDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SaveListDelete_WorkPerUser_AndSavingTheSameNameReplaces()
    {
        ChartLayoutService service = new(_dbContext);

        (await service.SaveAsync("u1", "Day trading", """{"panels":[]}""", CancellationToken.None)).Should().BeEmpty();
        (await service.SaveAsync("u1", "day trading", """{"panels":[1]}""", CancellationToken.None)).Should().BeEmpty();
        await service.SaveAsync("u2", "Outra", "{}", CancellationToken.None);

        IReadOnlyList<ChartLayoutDto> mine = await service.ListAsync("u1", CancellationToken.None);
        mine.Should().ContainSingle().Which.Json.Should().Contain("1");
        (await service.DeleteAsync("u1", "DAY TRADING", CancellationToken.None)).Should().BeTrue();
        (await service.DeleteAsync("u1", "nope", CancellationToken.None)).Should().BeFalse();
        (await service.ListAsync("u2", CancellationToken.None)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData("   ", "{}")]
    [InlineData("Nome", "isto não é json")]
    public async Task Save_InvalidInput_IsRejected(string name, string json) =>
        (await new ChartLayoutService(_dbContext).SaveAsync("u1", name, json, CancellationToken.None)).Should().NotBeEmpty();

    [Fact]
    public async Task Save_TooManyLayouts_IsRejected()
    {
        ChartLayoutService service = new(_dbContext);

        for (int i = 0; i < 20; i++)
        {
            (await service.SaveAsync("u1", $"L{i}", "{}", CancellationToken.None)).Should().BeEmpty();
        }

        (await service.SaveAsync("u1", "extra", "{}", CancellationToken.None)).Should().NotBeEmpty();
    }
}

public class ChartInsightsServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IJournalService _journal = Substitute.For<IJournalService>();
    private readonly IEvidenceSource _earnings = Substitute.For<IEvidenceSource>();
    private TraderDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
        _dbContext.Instruments.Add(new Instrument { Symbol = "EVTX", Name = "Event Inc", AssetClass = AssetClass.Stock, Currency = "USD", SortOrder = 9999 });
        Article strong = new() { Url = "https://x/1", UrlHash = "h1", TitleHash = "t1", Title = "Big news", Summary = "s", Source = "Reuters", SourceType = SourceType.News, PublishedAt = Start.AddHours(2), IngestedAt = Start, Sentiment = 0.8, Symbols = [new ArticleSymbol { Symbol = "EVTX" }] };
        Article weak = new() { Url = "https://x/2", UrlHash = "h2", TitleHash = "t2", Title = "Meh", Summary = "s", Source = "Reuters", SourceType = SourceType.News, PublishedAt = Start.AddHours(3), IngestedAt = Start, Sentiment = 0.05, Symbols = [new ArticleSymbol { Symbol = "EVTX" }] };
        _dbContext.Articles.AddRange(strong, weak);
        _dbContext.Suggestions.Add(new Suggestion { Symbol = "EVTX", Name = "Event Inc", Direction = TradeDirection.Buy, Confidence = 0.7, Entry = 10, StopLoss = 9, TakeProfit = 12, CreatedAt = Start.AddHours(4), ExpiresAt = Start.AddDays(5), Status = SuggestionStatus.Open });
        await _dbContext.SaveChangesAsync();

        _earnings.Name.Returns("Resultados (Finnhub)");
        _earnings.FetchAsync(Arg.Any<EvidenceQuery>(), Arg.Any<CancellationToken>()).Returns([new EvidenceItem(EvidenceKind.Earnings, CauseCategory.Earnings, "EPS 1,10 vs 1,40", string.Empty, "https://x/e", "Finnhub", Start.AddHours(1), 1.3, -0.5, true)]);
        _journal.ListAsync("u1", null, Arg.Any<CancellationToken>()).Returns(
        [
            new JournalTradeDto(1, "EVTX", "Event Inc", TradeDirection.Buy, Start.AddHours(5), 10, 1, 9, 12, 1, 0, "XTB", string.Empty, null, TradeStatus.Closed, Start.AddHours(6), 11, 1, 1, 1, null, null),
            new JournalTradeDto(2, "OTHER", "Other", TradeDirection.Buy, Start.AddHours(5), 10, 1, 9, 12, 1, 0, "XTB", string.Empty, null, TradeStatus.Open, null, null, null, null, null, null, null)
        ]);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetEvents_CollectsNewsEarningsSuggestionsAndOwnTradesInOrder()
    {
        ChartInsightsService service = new(_dbContext, Substitute.For<IMarketDataService>(), _journal, [_earnings]);

        IReadOnlyList<ChartEventDto> events = await service.GetEventsAsync("EVTX", "u1", Start, Start.AddDays(1), CancellationToken.None);

        events.Select(e => e.Kind).Should().Equal("earnings", "news", "suggestion", "trade", "trade");
        events.Select(e => e.Label).Should().Equal("Resultados", "Notícia", "Sugestão compra", "Entrada", "Saída");
        events.Should().BeInAscendingOrder(e => e.Time);
    }

    [Fact]
    public async Task GetEvents_IgnoresWeakNewsAndOtherSymbolsTrades()
    {
        ChartInsightsService service = new(_dbContext, Substitute.For<IMarketDataService>(), _journal, [_earnings]);

        IReadOnlyList<ChartEventDto> events = await service.GetEventsAsync("EVTX", "u1", Start, Start.AddDays(1), CancellationToken.None);

        events.Should().NotContain(e => e.Detail.Contains("Meh"));
        events.Count(e => e.Kind == "trade").Should().Be(2);
    }

    [Fact]
    public async Task GetEvents_WhenTheEarningsSourceFails_StillReturnsTheRest()
    {
        _earnings.FetchAsync(Arg.Any<EvidenceQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<EvidenceItem>>(new HttpRequestException("down")));
        ChartInsightsService service = new(_dbContext, Substitute.For<IMarketDataService>(), _journal, [_earnings]);

        IReadOnlyList<ChartEventDto> events = await service.GetEventsAsync("EVTX", "u1", Start, Start.AddDays(1), CancellationToken.None);

        events.Should().NotContain(e => e.Kind == "earnings");
        events.Should().Contain(e => e.Kind == "news");
    }

    [Fact]
    public async Task GetLevels_ComputesFromDailyAndIntradayCandles()
    {
        IMarketDataService marketData = Substitute.For<IMarketDataService>();
        List<Candle> daily = Enumerable.Range(0, 10).Select(i => new Candle(Start.AddDays(-12 + i), 10, 12 + i, 8, 11, 100)).ToList();
        marketData.GetCandlesAsync("EVTX", "1d", "3mo", Arg.Any<CancellationToken>()).Returns(daily);
        marketData.GetCandlesAsync("EVTX", "5m", "5d", Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<Candle>>(new HttpRequestException("down")));
        ChartInsightsService service = new(_dbContext, marketData, _journal, [_earnings]);

        IReadOnlyList<AutoLevel> levels = await service.GetLevelsAsync("EVTX", CancellationToken.None);

        levels.Should().Contain(l => l.Group == AutoLevelGroups.PreviousDay);
        levels.Should().NotContain(l => l.Group == AutoLevelGroups.OpeningRange);
    }
}
