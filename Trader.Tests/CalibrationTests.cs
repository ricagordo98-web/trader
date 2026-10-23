using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Persistence;
using Trader.Infrastructure.Suggestions;

namespace Trader.Tests;

public class ConfidenceCalibratorTests
{
    private static List<(double, bool)> Outcomes(double confidence, int count, double winRate) =>
        Enumerable.Range(0, count).Select(i => (confidence, (i < count * winRate))).ToList();

    [Fact]
    public void Fit_TooFewSamples_ReturnsNull() =>
        ConfidenceCalibrator.Fit(Outcomes(0.8, 29, 0.5)).Should().BeNull();

    [Fact]
    public void Fit_OverconfidentModel_PullsTheProbabilityDown()
    {
        List<(double, bool)> data = [.. Outcomes(0.62, 40, 0.5), .. Outcomes(0.85, 40, 0.55)];

        ConfidenceCalibration calibration = ConfidenceCalibrator.Fit(data)!;

        calibration.Samples.Should().Be(80);
        calibration.Apply(0.85).Should().BeApproximately(0.55, 0.06);
        calibration.Apply(0.85).Should().BeLessThan(0.85);
    }

    [Fact]
    public void Fit_ResultIsMonotoneEvenWhenHigherConfidenceDidWorse()
    {
        List<(double, bool)> data = [.. Outcomes(0.65, 40, 0.7), .. Outcomes(0.9, 40, 0.4)];

        ConfidenceCalibration calibration = ConfidenceCalibrator.Fit(data)!;

        calibration.Apply(0.9).Should().BeGreaterThanOrEqualTo(calibration.Apply(0.65) - 1e-9);
        calibration.Points.Select(p => p.Probability).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Apply_OutsideTheObservedRange_UsesTheNearestPointAndInterpolatesBetweenPoints()
    {
        ConfidenceCalibration calibration = new([new CalibrationPoint(0.6, 0.4), new CalibrationPoint(0.8, 0.6)], 100, 0.5);

        calibration.Apply(0.1).Should().Be(0.4);
        calibration.Apply(0.99).Should().Be(0.6);
        calibration.Apply(0.7).Should().BeApproximately(0.5, 1e-9);
    }
}

public class SuggestionCalibrationServiceTests : IAsyncLifetime
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

    private SuggestionService Service() => new(
        _dbContext,
        Substitute.For<IMarketDataService>(),
        Substitute.For<IScoringSettings>(),
        Substitute.For<IAlertService>(),
        new Trader.Core.Intraday.IntradayOptions(),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<SuggestionService>.Instance);

    private static Suggestion Make(SuggestionStatus status, double? r, double confidence = 0.8) => new()
    {
        Symbol = "TEST",
        Name = "Test",
        Direction = TradeDirection.Buy,
        Confidence = confidence,
        Entry = 100,
        StopLoss = 98,
        TakeProfit = 104,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        ExpiresAt = DateTimeOffset.UtcNow,
        Status = status,
        RMultiple = r,
        Horizon = SuggestionHorizon.Swing
    };

    [Fact]
    public async Task GetAsync_WithEnoughClosedSuggestions_AddsTheHistoricalHitRate()
    {
        for (int i = 0; i < 40; i++)
        {
            _dbContext.Suggestions.Add(i % 2 == 0 ? Make(SuggestionStatus.HitTakeProfit, 2) : Make(SuggestionStatus.HitStopLoss, -1));
        }

        _dbContext.Suggestions.Add(Make(SuggestionStatus.Open, null));
        await _dbContext.SaveChangesAsync();

        IReadOnlyList<SuggestionDto> open = await Service().GetAsync(null, true, 10, null, CancellationToken.None);

        SuggestionDto dto = open.Should().ContainSingle().Subject;
        dto.CalibratedConfidence.Should().BeApproximately(0.5, 0.05);
        dto.CalibrationSamples.Should().Be(40);
    }

    [Fact]
    public async Task GetAsync_WithoutHistory_LeavesTheCalibrationEmpty()
    {
        _dbContext.Suggestions.Add(Make(SuggestionStatus.Open, null));
        await _dbContext.SaveChangesAsync();

        (await Service().GetAsync(null, true, 10, null, CancellationToken.None)).Single().CalibratedConfidence.Should().BeNull();
    }
}
