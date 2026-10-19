using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Suggestions;

public sealed class SuggestionService(
    TraderDbContext dbContext,
    IMarketDataService marketData,
    IScoringSettings scoringSettings,
    ILogger<SuggestionService> logger) : ISuggestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        await EvaluateOpenAsync(cancellationToken);
        return await GenerateAsync(await scoringSettings.GetAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<SuggestionDto>> GetAsync(string? symbol, bool? open, int limit, CancellationToken cancellationToken)
    {
        IQueryable<Suggestion> query = dbContext.Suggestions.AsNoTracking();

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
        return rows.Select(ToDto).ToList();
    }

    public async Task<TrackRecordDto> GetTrackRecordAsync(CancellationToken cancellationToken)
    {
        List<Suggestion> closed = await dbContext.Suggestions
            .AsNoTracking()
            .Where(s => s.Status != SuggestionStatus.Open)
            .ToListAsync(cancellationToken);

        int openCount = await dbContext.Suggestions.CountAsync(s => s.Status == SuggestionStatus.Open, cancellationToken);

        return TrackRecordCalculator.Calculate(closed.Select(ToDto).ToList(), openCount);
    }

    private async Task EvaluateOpenAsync(CancellationToken cancellationToken)
    {
        List<Suggestion> open = await dbContext.Suggestions.Where(s => s.Status == SuggestionStatus.Open).ToListAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (Suggestion suggestion in open)
        {
            try
            {
                IReadOnlyList<Candle> candles = await marketData.GetCandlesAsync(suggestion.Symbol, "60m", "1mo", cancellationToken);
                OutcomeResult? outcome = OutcomeResolver.Resolve(suggestion, candles, now);

                if (outcome is null)
                {
                    continue;
                }

                suggestion.Status = outcome.Status;
                suggestion.ExitPrice = outcome.ExitPrice;
                suggestion.ClosedAt = outcome.ClosedAt;
                suggestion.RMultiple = outcome.RMultiple;
                logger.LogInformation("Suggestion {Id} {Symbol} closed as {Status} ({R:0.00}R)", suggestion.Id, suggestion.Symbol, outcome.Status, outcome.RMultiple);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Could not evaluate {Symbol}: {Message}", suggestion.Symbol, ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> GenerateAsync(ScoringOptions scoring, CancellationToken cancellationToken)
    {
        SuggestionEngine engine = new(scoring);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        HashSet<string> openSymbols = (await dbContext.Suggestions
            .Where(s => s.Status == SuggestionStatus.Open)
            .Select(s => s.Symbol)
            .ToListAsync(cancellationToken)).ToHashSet();

        List<Instrument> instruments = await dbContext.Instruments.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        int created = 0;

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

                SentimentSnapshot sentiment = await BuildSentimentAsync(instrument.Symbol, now, scoring, cancellationToken);
                SuggestionDraft? draft = engine.Evaluate(market, sentiment);

                if (draft is null)
                {
                    continue;
                }

                dbContext.Suggestions.Add(ToEntity(draft, now, scoring));
                created++;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Could not score {Symbol}: {Message}", instrument.Symbol, ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Suggestion cycle created {Count} suggestions", created);
        return created;
    }

    private async Task<SentimentSnapshot> BuildSentimentAsync(string symbol, DateTimeOffset now, ScoringOptions scoring, CancellationToken cancellationToken)
    {
        DateTimeOffset since = now.AddDays(-30);

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

    private static Suggestion ToEntity(SuggestionDraft draft, DateTimeOffset now, ScoringOptions scoring) => new()
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
        ExpiresAt = now.AddDays(scoring.ExpiryDays),
        Status = SuggestionStatus.Open
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
            evidence.Articles);
    }
}
