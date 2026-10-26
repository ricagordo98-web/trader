using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Suggestions;

public sealed class ModelReliabilityService(TraderDbContext dbContext, IMarketDataProvider provider, IMemoryCache cache) : IModelReliabilityService
{
    private const int StudyDays = 90;
    private const int MaxSymbols = 30;
    private const int MinArticlesPerSymbol = 2;
    private const int Concurrency = 3;
    private static readonly TimeSpan StudyCacheDuration = TimeSpan.FromHours(1);

    public async Task<ModelReliabilityReport> GetReportAsync(SuggestionHorizon? horizon, CancellationToken cancellationToken)
    {
        IQueryable<Suggestion> query = dbContext.Suggestions.AsNoTracking().Where(s => s.Status != SuggestionStatus.Open);

        if (horizon is not null)
        {
            query = query.Where(s => s.Horizon == horizon);
        }

        List<Suggestion> closed = await query.ToListAsync(cancellationToken);
        return ModelReliability.CalculateWithShadow(closed.Select(SuggestionService.ToDto).ToList());
    }

    public async Task<SentimentStudyResult> GetSentimentStudyAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue("sentiment-study", out SentimentStudyResult? cached) && cached is not null)
        {
            return cached;
        }

        DateTimeOffset since = DateTimeOffset.UtcNow.AddDays(-StudyDays);

        var rows = await (
            from link in dbContext.Set<ArticleSymbol>().AsNoTracking()
            join article in dbContext.Articles.AsNoTracking() on link.ArticleId equals article.Id
            where article.Sentiment != null && article.PublishedAt >= since
            orderby article.PublishedAt descending
            select new { link.Symbol, article.PublishedAt, Sentiment = article.Sentiment!.Value })
            .ToListAsync(cancellationToken);

        List<string> symbols = rows
            .GroupBy(r => r.Symbol)
            .Where(g => g.Count() >= MinArticlesPerSymbol)
            .OrderByDescending(g => g.Count())
            .Take(MaxSymbols)
            .Select(g => g.Key)
            .ToList();

        Dictionary<string, IReadOnlyList<Candle>> daily = [];
        using SemaphoreSlim gate = new(Concurrency);

        await Task.WhenAll(symbols.Select(async symbol =>
        {
            await gate.WaitAsync(cancellationToken);

            try
            {
                ChartSeries series = await provider.GetChartAsync(symbol, "1d", "6mo", cancellationToken);

                lock (daily)
                {
                    daily[symbol] = series.Candles;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                gate.Release();
            }
        }));

        List<SentimentObservation> observations = rows
            .Where(r => daily.ContainsKey(r.Symbol))
            .Select(r => new SentimentObservation(
                r.Symbol,
                r.Sentiment,
                SentimentStudy.ForwardReturn(daily[r.Symbol], r.PublishedAt, 1),
                SentimentStudy.ForwardReturn(daily[r.Symbol], r.PublishedAt, 3)))
            .ToList();

        SentimentStudyResult result = SentimentStudy.Evaluate(observations);
        cache.Set("sentiment-study", result, StudyCacheDuration);
        return result;
    }
}
