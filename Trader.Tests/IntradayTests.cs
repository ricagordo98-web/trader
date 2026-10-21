using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Persistence;
using Trader.Infrastructure.Suggestions;

namespace Trader.Tests;

internal static class IntradayFixtures
{
    public static readonly IntradayOptions Options = new();

    public static List<Candle> Day(DateTime date, double open, int count, Func<int, double> closeAt, double volume)
    {
        List<Candle> candles = [];
        DateTimeOffset start = new(date.Date.AddHours(14).AddMinutes(30), TimeSpan.Zero);
        double previous = open;

        for (int i = 0; i < count; i++)
        {
            double close = closeAt(i);
            candles.Add(new Candle(start.AddMinutes(5 * i), previous, Math.Max(previous, close) + 0.1, Math.Min(previous, close) - 0.1, close, volume));
            previous = close;
        }

        return candles;
    }

    public static List<Candle> Rally(out DateTimeOffset now, double historyVolume = 1000, double todayVolume = 3000)
    {
        List<Candle> candles = [];
        candles.AddRange(Day(new DateTime(2026, 10, 15), 100, 78, _ => 100, historyVolume));
        candles.AddRange(Day(new DateTime(2026, 10, 16), 100, 78, _ => 100, historyVolume));
        candles.AddRange(Day(new DateTime(2026, 10, 19), 100, 78, _ => 100, historyVolume));
        List<Candle> today = Day(new DateTime(2026, 10, 20), 102, 18, i => i < 9 ? 102 + 0.03 * i : 102.3 + 0.12 * (i - 8), todayVolume);
        candles.AddRange(today);
        now = today[^1].Time.AddMinutes(1);
        return candles;
    }

    public static IntradayContext Context(List<Candle> candles, DateTimeOffset now, AssetClass assetClass = AssetClass.Stock) =>
        IntradayAnalyzer.Analyze("TEST", "Test Inc", assetClass, candles, now, Options)!;

    public static SentimentSnapshot NoNews() => new(0, 0, null, []);
}

public class IntradayAnalyzerTests
{
    [Fact]
    public void Analyze_StaleLastCandle_ReturnsNull()
    {
        List<Candle> candles = IntradayFixtures.Rally(out DateTimeOffset now);

        IntradayAnalyzer.Analyze("TEST", "Test", AssetClass.Stock, candles, now.AddHours(3), IntradayFixtures.Options).Should().BeNull();
    }

    [Fact]
    public void Analyze_Rally_ComputesSessionReferences()
    {
        List<Candle> candles = IntradayFixtures.Rally(out DateTimeOffset now);

        IntradayContext context = IntradayFixtures.Context(candles, now);

        context.GapPercent.Should().BeApproximately(2.0, 0.01);
        context.PreviousDayHigh.Should().BeApproximately(100.1, 0.001);
        context.PreviousDayClose.Should().Be(100);
        context.RelativeVolume.Should().BeApproximately(3.0, 0.01);
        context.OpeningRangeHigh.Should().NotBeNull();
        context.Vwap.Should().BeInRange(context.DayOpen, context.Price);
        context.RecentMove.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Analyze_ForexWithoutVolume_HasNoVwapVolumeOrSessionLevels()
    {
        List<Candle> candles = IntradayFixtures.Rally(out DateTimeOffset now, 0, 0);

        IntradayContext context = IntradayFixtures.Context(candles, now, AssetClass.Forex);

        context.Vwap.Should().BeNull();
        context.RelativeVolume.Should().BeNull();
        context.GapPercent.Should().BeNull();
        context.OpeningRangeHigh.Should().BeNull();
        context.PreviousDayHigh.Should().NotBeNull();
    }

    [Fact]
    public void Analyze_TooFewHistoryDays_HasNoRelativeVolume()
    {
        List<Candle> candles = IntradayFixtures.Rally(out DateTimeOffset now).Where(c => c.Time.UtcDateTime.Date >= new DateTime(2026, 10, 19)).ToList();

        IntradayFixtures.Context(candles, now).RelativeVolume.Should().BeNull();
    }
}

public class IntradayEngineTests
{
    private readonly IntradayEngine _engine = new(IntradayFixtures.Options);

    [Fact]
    public void DetectEvents_GapBreakoutAndVolumeSpike_AreAllReported()
    {
        IntradayContext context = IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now), now);

        IReadOnlyList<RadarEvent> events = _engine.DetectEvents(context);

        events.Select(e => e.Kind).Should().Contain([
            RadarEventKind.VolumeSpike,
            RadarEventKind.Gap,
            RadarEventKind.OpeningRangeBreakout]);
        events.Should().OnlyContain(e => e.Direction == TradeDirection.Buy && e.Strength > 0 && e.Strength <= 1);
    }

    [Fact]
    public void DetectEvents_PreviousDayBreakout_OnlyWhenCrossedRecently()
    {
        List<Candle> history = IntradayFixtures.Rally(out _).Where(c => c.Time.UtcDateTime.Date < new DateTime(2026, 10, 20)).ToList();
        List<Candle> fresh = IntradayFixtures.Day(new DateTime(2026, 10, 20), 100, 18, i => i < 12 ? 100 : 100 + 0.15 * (i - 11), 3000);
        List<Candle> stale = IntradayFixtures.Day(new DateTime(2026, 10, 20), 100.5, 60, i => 100.5 + 0.01 * i, 3000);

        _engine.DetectEvents(IntradayFixtures.Context([.. history, .. fresh], fresh[^1].Time.AddMinutes(1))).Select(e => e.Kind)
            .Should().Contain(RadarEventKind.PreviousDayBreakout);
        _engine.DetectEvents(IntradayFixtures.Context([.. history, .. stale], stale[^1].Time.AddMinutes(1))).Select(e => e.Kind)
            .Should().NotContain(RadarEventKind.PreviousDayBreakout);
    }

    [Fact]
    public void Evaluate_NoRecentMoveInTheSuggestedDirection_ReturnsNull()
    {
        List<Candle> candles = IntradayFixtures.Rally(out DateTimeOffset now);
        List<Candle> today = candles.Where(c => c.Time.UtcDateTime.Date == new DateTime(2026, 10, 20)).ToList();
        List<Candle> flat = candles.Where(c => c.Time.UtcDateTime.Date < new DateTime(2026, 10, 20))
            .Concat(today.Take(12))
            .Concat(Enumerable.Range(0, 6).Select(i => today[11] with { Time = today[11].Time.AddMinutes(5 * (i + 1)) }))
            .ToList();

        IntradayContext context = IntradayFixtures.Context(flat, flat[^1].Time.AddMinutes(1));

        _engine.Evaluate(context, IntradayFixtures.NoNews()).Should().BeNull();
    }

    [Fact]
    public void DetectEvents_QuietSession_ReportsNothing()
    {
        List<Candle> candles = [];
        candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 16), 100, 78, _ => 100, 1000));
        candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 19), 100, 78, _ => 100, 1000));
        List<Candle> today = IntradayFixtures.Day(new DateTime(2026, 10, 20), 100, 18, _ => 100, 1000);
        candles.AddRange(today);

        _engine.DetectEvents(IntradayFixtures.Context(candles, today[^1].Time.AddMinutes(1))).Should().BeEmpty();
    }

    [Fact]
    public void DetectEvents_BreakoutWithoutVolume_IsIgnored()
    {
        List<Candle> candles = IntradayFixtures.Rally(out DateTimeOffset now, 1000, 700);

        _engine.DetectEvents(IntradayFixtures.Context(candles, now)).Select(e => e.Kind)
            .Should().NotContain([RadarEventKind.OpeningRangeBreakout, RadarEventKind.PreviousDayBreakout]);
    }

    [Fact]
    public void Evaluate_ConfirmedBreakout_SuggestsBuyWithProtectiveLevels()
    {
        IntradayContext context = IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now), now);

        SuggestionDraft? draft = _engine.Evaluate(context, IntradayFixtures.NoNews());

        draft.Should().NotBeNull();
        draft!.Direction.Should().Be(TradeDirection.Buy);
        draft.StopLoss.Should().BeLessThan(draft.Entry);
        draft.TakeProfit.Should().BeGreaterThan(draft.Entry);
        draft.RiskReward.Should().BeGreaterThanOrEqualTo(1.5);
        draft.Confidence.Should().BeInRange(IntradayFixtures.Options.ConfidenceThreshold, IntradayFixtures.Options.MaxConfidence);
        draft.Signals.Select(s => s.Name).Should().Contain(["vwap", "opening-range", "relative-volume", "previous-day", "gap"]);
    }

    [Fact]
    public void Evaluate_LowRelativeVolume_ReturnsNull()
    {
        IntradayContext context = IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now, 1000, 1000), now);

        _engine.Evaluate(context, IntradayFixtures.NoNews()).Should().BeNull();
    }

    [Fact]
    public void Evaluate_Selloff_SuggestsSellWithMirroredLevels()
    {
        List<Candle> candles = [];
        candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 15), 100, 78, _ => 100, 1000));
        candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 16), 100, 78, _ => 100, 1000));
        candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 19), 100, 78, _ => 100, 1000));
        List<Candle> today = IntradayFixtures.Day(new DateTime(2026, 10, 20), 98, 18, i => i < 6 ? 98 - 0.05 * i : 97.7 - 0.1 * (i - 5), 3000);
        candles.AddRange(today);

        SuggestionDraft? draft = _engine.Evaluate(IntradayFixtures.Context(candles, today[^1].Time.AddMinutes(1)), IntradayFixtures.NoNews());

        draft.Should().NotBeNull();
        draft!.Direction.Should().Be(TradeDirection.Sell);
        draft.StopLoss.Should().BeGreaterThan(draft.Entry);
        draft.TakeProfit.Should().BeLessThan(draft.Entry);
    }

    [Fact]
    public void Evaluate_NewsFollowedByPriceReaction_AddsCatalystSignal()
    {
        IntradayContext context = IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now), now);
        EvidenceArticle article = new("Company wins major contract", "https://x.test/a", "Reuters", 0.8, context.SessionCandles[8].Time.AddMinutes(1), 0.8);
        SentimentSnapshot sentiment = new(0.8, 1, null, [article]);

        SuggestionDraft draft = _engine.Evaluate(context, sentiment)!;

        draft.Signals.Should().Contain(s => s.Name == "catalyst" && s.Value > 0);
        draft.Signals.Should().Contain(s => s.Name == "recent-news");
        draft.Articles.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_NewsAgainstThePriceMove_DoesNotCountAsCatalyst()
    {
        IntradayContext context = IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now), now);
        EvidenceArticle article = new("Company hit by lawsuit", "https://x.test/b", "Reuters", -0.8, context.SessionCandles[8].Time.AddMinutes(1), 0.8);

        SuggestionDraft? draft = _engine.Evaluate(context, new SentimentSnapshot(-0.8, 1, null, [article]));

        draft?.Signals.Should().NotContain(s => s.Name == "catalyst");
    }
}

public class RadarAlertTests
{
    private static RadarEvent Make(string symbol, double strength) =>
        new(symbol, symbol, RadarEventKind.VolumeSpike, TradeDirection.Buy, strength, "volume 4,0× acima do normal", 10, DateTimeOffset.UtcNow);

    [Fact]
    public void ForRadarEvents_FewEvents_AreAlertedIndividually()
    {
        IReadOnlyList<Alert> alerts = AlertFactory.ForRadarEvents([Make("AAA", 0.6), Make("BBB", 0.7)], DateTimeOffset.UtcNow);

        alerts.Should().HaveCount(2).And.OnlyContain(a => a.Kind == AlertKind.Radar);
    }

    [Fact]
    public void ForRadarEvents_ManyEvents_AreCoalesced()
    {
        List<RadarEvent> many = Enumerable.Range(0, 9).Select(i => Make($"S{i}", 0.5 + i / 20.0)).ToList();

        IReadOnlyList<Alert> alerts = AlertFactory.ForRadarEvents(many, DateTimeOffset.UtcNow);

        alerts.Should().ContainSingle().Which.Title.Should().Be("Radar: 9 movimentos anormais");
    }

    [Fact]
    public void ForRadarEvents_NoEvents_ReturnsNothing() =>
        AlertFactory.ForRadarEvents([], DateTimeOffset.UtcNow).Should().BeEmpty();
}

public class IntradaySuggestionServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private TraderDbContext _dbContext = null!;
    private readonly IAlertService _alerts = Substitute.For<IAlertService>();
    private readonly IMarketDataService _marketData = Substitute.For<IMarketDataService>();

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

    private SuggestionService CreateService() =>
        new(_dbContext, _marketData, Substitute.For<IScoringSettings>(), _alerts, IntradayFixtures.Options, NullLogger<SuggestionService>.Instance);

    private static IntradayContext RallyContext() =>
        IntradayFixtures.Context(IntradayFixtures.Rally(out DateTimeOffset now), now);

    [Fact]
    public async Task RefreshIntraday_ConfirmedSetup_CreatesIntradaySuggestionExpiringInHours()
    {
        int created = await CreateService().RefreshIntradayAsync([RallyContext()], CancellationToken.None);

        created.Should().Be(1);
        Suggestion stored = await _dbContext.Suggestions.SingleAsync();
        stored.Horizon.Should().Be(SuggestionHorizon.Intraday);
        stored.Status.Should().Be(SuggestionStatus.Open);
        (stored.ExpiresAt - stored.CreatedAt).Should().Be(TimeSpan.FromHours(IntradayFixtures.Options.ExpiryHours));
        await _alerts.Received(1).RaiseAsync(Arg.Is<IReadOnlyList<Alert>>(a => a.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshIntraday_SecondRunWithinCooldown_DoesNotDuplicate()
    {
        SuggestionService service = CreateService();
        await service.RefreshIntradayAsync([RallyContext()], CancellationToken.None);

        int created = await service.RefreshIntradayAsync([RallyContext()], CancellationToken.None);

        created.Should().Be(0);
        (await _dbContext.Suggestions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RefreshIntraday_WhenOpenLimitReached_CreatesNothing()
    {
        for (int i = 0; i < IntradayFixtures.Options.MaxOpenSuggestions; i++)
        {
            _dbContext.Suggestions.Add(new Suggestion
            {
                Symbol = $"OPEN{i}",
                Name = "Open",
                Entry = 10,
                StopLoss = 9,
                TakeProfit = 12,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Status = SuggestionStatus.Open,
                Horizon = SuggestionHorizon.Intraday
            });
        }

        await _dbContext.SaveChangesAsync();

        (await CreateService().RefreshIntradayAsync([RallyContext()], CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_FiltersByHorizon()
    {
        SuggestionService service = CreateService();
        await service.RefreshIntradayAsync([RallyContext()], CancellationToken.None);
        _dbContext.Suggestions.Add(new Suggestion
        {
            Symbol = "SWING",
            Name = "Swing Corp",
            Direction = TradeDirection.Buy,
            Entry = 10,
            StopLoss = 9,
            TakeProfit = 12,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(5),
            Status = SuggestionStatus.Open,
            Horizon = SuggestionHorizon.Swing
        });
        await _dbContext.SaveChangesAsync();

        (await service.GetAsync(null, true, 50, SuggestionHorizon.Intraday, CancellationToken.None)).Should().ContainSingle().Which.Symbol.Should().Be("TEST");
        (await service.GetAsync(null, true, 50, SuggestionHorizon.Swing, CancellationToken.None)).Should().ContainSingle().Which.Symbol.Should().Be("SWING");
        (await service.GetAsync(null, true, 50, null, CancellationToken.None)).Should().HaveCount(2);
    }
}
