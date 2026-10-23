using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Explain;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Intraday;

public sealed class RadarService(IServiceScopeFactory scopeFactory, IntradayOptions options, ILogger<RadarService> logger) : IRadarService
{
    private const int MaxParallelFetches = 4;
    private const int MaxRetainedEvents = 300;
    private const double AlertStrength = 0.5;
    private const int MaxExplainedPerScan = 3;
    private static readonly TimeSpan ExplainTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan EventRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan ClosedMarketRecheck = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClosedMarketAge = TimeSpan.FromHours(2);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recheckAt = new();
    private readonly Dictionary<string, DateTimeOffset> _fired = [];
    private List<RadarEvent> _events = [];
    private RadarSnapshot _snapshot = new(DateTimeOffset.MinValue, 0, [], []);

    public RadarSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (!await _scanLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            TraderDbContext dbContext = scope.ServiceProvider.GetRequiredService<TraderDbContext>();
            IMarketDataService marketData = scope.ServiceProvider.GetRequiredService<IMarketDataService>();

            List<Instrument> instruments = await dbContext.Instruments.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<IntradayContext> contexts = await LoadContextsAsync(marketData, instruments, now, cancellationToken);

            IntradayEngine engine = new(options);
            List<RadarEvent> fresh = Register(contexts.SelectMany(engine.DetectEvents).ToList(), now);

            Publish(now, instruments.Count, contexts, fresh);

            List<RadarEvent> explained = await ExplainAsync(scope.ServiceProvider.GetRequiredService<IMoveExplainer>(), fresh, cancellationToken);

            if (!ReferenceEquals(explained, fresh))
            {
                Attach(explained);
                Publish(now, instruments.Count, contexts, []);
            }

            await EvaluateRulesAsync(scope.ServiceProvider.GetRequiredService<IAlertRuleService>(), explained, now, cancellationToken);

            IAlertService alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
            await alerts.RaiseAsync(AlertFactory.ForRadarEvents(explained.Where(e => e.Strength >= AlertStrength).ToList(), now), cancellationToken);

            await scope.ServiceProvider.GetRequiredService<ISuggestionService>().RefreshIntradayAsync(contexts, cancellationToken);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task<List<RadarEvent>> ExplainAsync(IMoveExplainer explainer, List<RadarEvent> fresh, CancellationToken cancellationToken)
    {
        Dictionary<string, string> reasons = [];

        foreach (RadarEvent radarEvent in fresh
            .Where(e => e.Strength >= AlertStrength && e.Kind is RadarEventKind.Gap or RadarEventKind.VolumeSpike or RadarEventKind.MomentumBurst)
            .OrderByDescending(e => e.Strength)
            .DistinctBy(e => e.Symbol)
            .Take(MaxExplainedPerScan))
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ExplainTimeout);

            try
            {
                MoveExplanation? explanation = await explainer.ExplainAsync(new MoveRequest(radarEvent.Symbol, null, null, false, false), timeout.Token);

                if (explanation is not null && ExplanationText.OneLiner(explanation) is { } why)
                {
                    reasons[radarEvent.Symbol] = why;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Radar could not explain {Symbol}: {Message}", radarEvent.Symbol, ex.Message);
            }
        }

        return reasons.Count == 0
            ? fresh
            : fresh.Select(e => reasons.TryGetValue(e.Symbol, out string? why) ? e with { Why = why } : e).ToList();
    }

    private async Task EvaluateRulesAsync(IAlertRuleService rules, List<RadarEvent> fresh, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            RadarSnapshot snapshot = GetSnapshot();
            await rules.EvaluateAsync(snapshot.Board, snapshot.Events.Concat(fresh).DistinctBy(e => (e.Symbol, e.Kind, e.Direction, e.Time)).ToList(), now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Alert rules evaluation failed: {Message}", ex.Message);
        }
    }

    private void Attach(List<RadarEvent> explained)
    {
        lock (_gate)
        {
            _events = _events.Select(e => explained.FirstOrDefault(x => x.Symbol == e.Symbol && x.Kind == e.Kind && x.Direction == e.Direction && x.Time == e.Time) ?? e).ToList();
        }
    }

    private async Task<List<IntradayContext>> LoadContextsAsync(IMarketDataService marketData, List<Instrument> instruments, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using SemaphoreSlim throttle = new(MaxParallelFetches);

        IntradayContext?[] results = await Task.WhenAll(instruments
            .Where(i => !_recheckAt.TryGetValue(i.Symbol, out DateTimeOffset next) || next <= now)
            .Select(async instrument =>
            {
                await throttle.WaitAsync(cancellationToken);

                try
                {
                    IReadOnlyList<Candle> candles = await marketData.GetCandlesAsync(instrument.Symbol, "5m", "5d", cancellationToken);
                    IntradayContext? context = IntradayAnalyzer.Analyze(instrument.Symbol, instrument.Name, instrument.AssetClass, candles, now, options);

                    if (context is null && (candles.Count == 0 || now - candles[^1].Time > ClosedMarketAge))
                    {
                        _recheckAt[instrument.Symbol] = now + ClosedMarketRecheck;
                    }
                    else
                    {
                        _recheckAt.TryRemove(instrument.Symbol, out _);
                    }

                    return context;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug("Radar could not load {Symbol}: {Message}", instrument.Symbol, ex.Message);
                    return null;
                }
                finally
                {
                    throttle.Release();
                }
            }));

        return results.OfType<IntradayContext>().ToList();
    }

    private List<RadarEvent> Register(List<RadarEvent> detected, DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (string stale in _fired.Where(f => now - f.Value > TimeSpan.FromDays(2)).Select(f => f.Key).ToList())
            {
                _fired.Remove(stale);
            }

            List<RadarEvent> fresh = [];

            foreach (RadarEvent radarEvent in detected)
            {
                string key = $"{radarEvent.Symbol}|{radarEvent.Kind}|{radarEvent.Direction}|{radarEvent.Time.UtcDateTime:yyyyMMdd}";

                if (_fired.TryAdd(key, now))
                {
                    fresh.Add(radarEvent);
                }
            }

            _events = fresh
                .Concat(_events)
                .Where(e => now - e.Time <= EventRetention)
                .Take(MaxRetainedEvents)
                .ToList();

            return fresh;
        }
    }

    private void Publish(DateTimeOffset now, int instrumentCount, List<IntradayContext> contexts, List<RadarEvent> fresh)
    {
        List<RadarRow> board = contexts
            .Select(c => new RadarRow(
                c.Symbol,
                c.Name,
                c.AssetClass,
                c.Price,
                c.PreviousDayClose is > 0 ? (c.Price - c.PreviousDayClose.Value) / c.PreviousDayClose.Value * 100 : (c.Price - c.DayOpen) / c.DayOpen * 100,
                c.GapPercent,
                c.RelativeVolume,
                c.Vwap is > 0 ? (c.Price - c.Vwap.Value) / c.Vwap.Value * 100 : null,
                c.LastCandleTime))
            .ToList();

        lock (_gate)
        {
            _snapshot = new RadarSnapshot(now, instrumentCount, _events, board);
        }

        if (fresh.Count > 0)
        {
            logger.LogInformation("Radar detected {Count} new events", fresh.Count);
        }
    }
}
