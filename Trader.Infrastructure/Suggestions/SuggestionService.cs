using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Suggestions;

public sealed class SuggestionService(
    TraderDbContext dbContext,
    IMarketDataService marketData,
    IScoringSettings scoringSettings,
    IAlertService alerts,
    IntradayOptions intradayOptions,
    IMemoryCache cache,
    ILogger<SuggestionService> logger) : ISuggestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan IntradayCooldown = TimeSpan.FromHours(1);
    private static readonly TimeSpan CalibrationCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SwingNewsWindow = TimeSpan.FromDays(30);

    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        await EvaluateOpenAsync(SuggestionHorizon.Swing, cancellationToken);
        return await GenerateAsync(await scoringSettings.GetAsync(cancellationToken), cancellationToken);
    }

    public async Task<int> RefreshIntradayAsync(IReadOnlyList<IntradayContext> contexts, CancellationToken cancellationToken)
    {
        await EvaluateOpenAsync(SuggestionHorizon.Intraday, cancellationToken);

        IntradayEngine engine = new(intradayOptions);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset cooldownStart = now - IntradayCooldown;

        HashSet<string> blocked = (await dbContext.Suggestions
            .Where(s => s.Horizon == SuggestionHorizon.Intraday && (s.Status == SuggestionStatus.Open || s.CreatedAt >= cooldownStart))
            .Select(s => s.Symbol)
            .ToListAsync(cancellationToken)).ToHashSet();

        int slots = intradayOptions.MaxOpenSuggestions
            - await dbContext.Suggestions.CountAsync(x => x.Horizon == SuggestionHorizon.Intraday && x.Status == SuggestionStatus.Open, cancellationToken);

        if (slots <= 0)
        {
            return 0;
        }

        ScoringOptions newsScoring = new() { SentimentHalfLifeHours = intradayOptions.NewsHalfLifeHours };
        List<(SuggestionDraft Draft, IntradayContext Context)> drafts = [];

        foreach (IntradayContext context in contexts.Where(c => !blocked.Contains(c.Symbol) && Math.Abs(c.RecentMove) >= intradayOptions.MinEntryMove))
        {
            SentimentSnapshot sentiment = await BuildSentimentAsync(context.Symbol, now, newsScoring, TimeSpan.FromHours(intradayOptions.NewsWindowHours), cancellationToken);
            SuggestionDraft? draft = engine.Evaluate(context, sentiment);

            if (draft is not null)
            {
                drafts.Add((draft, context));
            }
        }

        List<Suggestion> added = drafts
            .OrderByDescending(d => d.Draft.Confidence)
            .Take(Math.Min(slots, intradayOptions.MaxNewSuggestionsPerCycle))
            .Select(d => ToEntity(d.Draft, now, now.AddHours(intradayOptions.ExpiryHours), SuggestionHorizon.Intraday))
            .ToList();

        dbContext.Suggestions.AddRange(added);
        await dbContext.SaveChangesAsync(cancellationToken);
        await alerts.RaiseAsync(AlertFactory.ForNewSuggestions(added, now), cancellationToken);

        if (added.Count > 0)
        {
            logger.LogInformation("Intraday cycle created {Count} suggestions", added.Count);
        }

        return added.Count;
    }

    public async Task<IReadOnlyList<SuggestionDto>> GetAsync(string? symbol, bool? open, int limit, SuggestionHorizon? horizon, CancellationToken cancellationToken)
    {
        IQueryable<Suggestion> query = dbContext.Suggestions.AsNoTracking();

        if (horizon is not null)
        {
            query = query.Where(s => s.Horizon == horizon);
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            query = query.Where(s => s.Symbol == symbol);
        }

        query = open switch
        {
            true => query.Where(s => s.Status == SuggestionStatus.Open).OrderByDescending(s => s.Confidence),
            false => query.Where(s => s.Status != SuggestionStatus.Open).OrderByDescending(s => s.ClosedAt),
            _ => query.OrderByDescending(s => s.CreatedAt)
        };

        List<Suggestion> rows = await query.Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken);
        Dictionary<SuggestionHorizon, ConfidenceCalibration?> calibrations = [];

        foreach (SuggestionHorizon rowHorizon in rows.Select(r => r.Horizon).Distinct())
        {
            calibrations[rowHorizon] = await GetCalibrationAsync(rowHorizon, cancellationToken);
        }

        return rows
            .Select(r => ToDto(r) is var dto && calibrations[r.Horizon] is { } calibration
                ? dto with { CalibratedConfidence = calibration.Apply(r.Confidence), CalibrationSamples = calibration.Samples }
                : ToDto(r))
            .ToList();
    }

    public async Task<TrackRecordDto> GetTrackRecordAsync(SuggestionHorizon? horizon, CancellationToken cancellationToken)
    {
        IQueryable<Suggestion> all = dbContext.Suggestions.AsNoTracking();

        if (horizon is not null)
        {
            all = all.Where(s => s.Horizon == horizon);
        }

        List<Suggestion> closed = await all
            .Where(s => s.Status != SuggestionStatus.Open)
            .ToListAsync(cancellationToken);

        int openCount = await all.CountAsync(s => s.Status == SuggestionStatus.Open, cancellationToken);

        return TrackRecordCalculator.Calculate(closed.Select(ToDto).ToList(), openCount);
    }

    private async Task<ConfidenceCalibration?> GetCalibrationAsync(SuggestionHorizon horizon, CancellationToken cancellationToken)
    {
        string key = $"calibration:{horizon}";

        if (cache.TryGetValue(key, out ConfidenceCalibration? cached))
        {
            return cached;
        }

        var closed = await dbContext.Suggestions
            .AsNoTracking()
            .Where(s => s.Horizon == horizon && s.Status != SuggestionStatus.Open && s.RMultiple != null)
            .Select(s => new { s.Confidence, R = s.RMultiple!.Value })
            .ToListAsync(cancellationToken);

        ConfidenceCalibration? calibration = ConfidenceCalibrator.Fit(closed.Select(c => (c.Confidence, c.R > 0)).ToList());
        cache.Set(key, calibration, CalibrationCacheDuration);
        return calibration;
    }

    private async Task EvaluateOpenAsync(SuggestionHorizon horizon, CancellationToken cancellationToken)
    {
        List<Suggestion> open = await dbContext.Suggestions
            .Where(s => s.Status == SuggestionStatus.Open && s.Horizon == horizon)
            .ToListAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<Alert> raised = [];

        foreach (Suggestion suggestion in open)
        {
            try
            {
                IReadOnlyList<Candle> candles = horizon == SuggestionHorizon.Intraday
                    ? await marketData.GetCandlesAsync(suggestion.Symbol, "5m", "5d", cancellationToken)
                    : await marketData.GetCandlesAsync(suggestion.Symbol, "60m", "1mo", cancellationToken);
                OutcomeResult? outcome = OutcomeResolver.Resolve(suggestion, candles, now);

                if (outcome is null)
                {
                    continue;
                }

                suggestion.Status = outcome.Status;
                suggestion.ExitPrice = outcome.ExitPrice;
                suggestion.ClosedAt = outcome.ClosedAt;
                suggestion.RMultiple = outcome.RMultiple;

                if (AlertFactory.ForOutcome(suggestion, now) is { } alert)
                {
                    raised.Add(alert);
                }

                logger.LogInformation("Suggestion {Id} {Symbol} closed as {Status} ({R:0.00}R)", suggestion.Id, suggestion.Symbol, outcome.Status, outcome.RMultiple);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Could not evaluate {Symbol}: {Message}", suggestion.Symbol, ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await alerts.RaiseAsync(raised, cancellationToken);
    }

    private async Task<int> GenerateAsync(ScoringOptions scoring, CancellationToken cancellationToken)
    {
        SuggestionEngine engine = new(scoring);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        HashSet<string> openSymbols = (await dbContext.Suggestions
            .Where(s => s.Status == SuggestionStatus.Open && s.Horizon == SuggestionHorizon.Swing)
            .Select(s => s.Symbol)
            .ToListAsync(cancellationToken)).ToHashSet();

        List<Instrument> instruments = await dbContext.Instruments.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        int created = 0;
        List<Suggestion> added = [];

        foreach (Instrument instrument in instruments.Where(i => !openSymbols.Contains(i.Symbol)))
        {
            try
            {
                IReadOnlyList<Candle> candles = await marketData.GetCandlesAsync(instrument.Symbol, "1d", "1y", cancellationToken);
                MarketSnapshot? market = IndicatorCalculator.Compute(instrument.Symbol, instrument.Name, candles);

                if (market is null)
                {
                    continue;
                }

                SentimentSnapshot sentiment = await BuildSentimentAsync(instrument.Symbol, now, scoring, SwingNewsWindow, cancellationToken);
                SuggestionDraft? draft = engine.Evaluate(market, sentiment);

                if (draft is null)
                {
                    continue;
                }

                Suggestion entity = ToEntity(draft, now, now.AddDays(scoring.ExpiryDays), SuggestionHorizon.Swing);
                dbContext.Suggestions.Add(entity);
                added.Add(entity);
                created++;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Could not score {Symbol}: {Message}", instrument.Symbol, ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await alerts.RaiseAsync(AlertFactory.ForNewSuggestions(added, now), cancellationToken);
        logger.LogInformation("Suggestion cycle created {Count} suggestions", created);
        return created;
    }

    private async Task<SentimentSnapshot> BuildSentimentAsync(string symbol, DateTimeOffset now, ScoringOptions scoring, TimeSpan window, CancellationToken cancellationToken)
    {
        DateTimeOffset since = now - window;

        var rows = await (
            from articleSymbol in dbContext.Set<ArticleSymbol>().AsNoTracking()
            join article in dbContext.Articles.AsNoTracking() on articleSymbol.ArticleId equals article.Id
            where articleSymbol.Symbol == symbol && article.PublishedAt >= since && article.Sentiment != null
            select new { article.Title, article.Url, article.Source, article.SourceType, Sentiment = article.Sentiment!.Value, article.PublishedAt })
            .ToListAsync(cancellationToken);

        return SentimentAggregator.Build(
            rows.Select(r => new ArticleSentimentRow(r.Title, r.Url, r.Source, r.SourceType, r.Sentiment, r.PublishedAt)).ToList(),
            now,
            scoring);
    }

    private static Suggestion ToEntity(SuggestionDraft draft, DateTimeOffset now, DateTimeOffset expiresAt, SuggestionHorizon horizon) => new()
    {
        Symbol = draft.Symbol,
        Name = draft.Name,
        Direction = draft.Direction,
        Confidence = draft.Confidence,
        Score = draft.Score,
        Entry = draft.Entry,
        StopLoss = draft.StopLoss,
        TakeProfit = draft.TakeProfit,
        RiskReward = draft.RiskReward,
        Explanation = draft.Explanation,
        EvidenceJson = JsonSerializer.Serialize(new SuggestionEvidence(draft.Signals, draft.Articles), JsonOptions),
        CreatedAt = now,
        ExpiresAt = expiresAt,
        Status = SuggestionStatus.Open,
        Horizon = horizon
    };

    private static SuggestionDto ToDto(Suggestion s)
    {
        SuggestionEvidence evidence = JsonSerializer.Deserialize<SuggestionEvidence>(s.EvidenceJson, JsonOptions)
            ?? new SuggestionEvidence([], []);

        return new SuggestionDto(
            s.Id,
            s.Symbol,
            s.Name,
            s.Direction,
            s.Confidence,
            s.Entry,
            s.StopLoss,
            s.TakeProfit,
            s.RiskReward,
            s.Explanation,
            s.CreatedAt,
            s.ExpiresAt,
            s.Status,
            s.ClosedAt,
            s.ExitPrice,
            s.RMultiple,
            evidence.Signals,
            evidence.Articles,
            s.Horizon);
    }
}
