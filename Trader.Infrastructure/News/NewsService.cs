using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.News;

public sealed class NewsService(TraderDbContext dbContext) : INewsService
{
    public async Task<IReadOnlyList<ArticleDto>> GetArticlesAsync(NewsQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Article> articles = dbContext.Articles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            string symbol = query.Symbol;
            articles = articles.Where(a => a.Symbols.Any(s => s.Symbol == symbol));
        }

        if (query.SourceType is { } sourceType)
        {
            articles = articles.Where(a => a.SourceType == sourceType);
        }

        articles = query.Sentiment?.ToLowerInvariant() switch
        {
            "positive" => articles.Where(a => a.Sentiment > SentimentBands.Threshold),
            "negative" => articles.Where(a => a.Sentiment < -SentimentBands.Threshold),
            "neutral" => articles.Where(a => a.Sentiment >= -SentimentBands.Threshold && a.Sentiment <= SentimentBands.Threshold),
            _ => articles
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{query.Search.Trim()}%";
            articles = articles.Where(a => EF.Functions.Like(a.Title, pattern) || EF.Functions.Like(a.Summary, pattern));
        }

        if (query.Before is { } before)
        {
            articles = articles.Where(a => a.PublishedAt < before);
        }

        List<Article> result = await articles
            .Include(a => a.Symbols)
            .OrderByDescending(a => a.PublishedAt)
            .Take(Math.Clamp(query.Limit, 1, 200))
            .ToListAsync(cancellationToken);

        return result
            .Select(a => new ArticleDto(
                a.Id,
                a.Title,
                a.Summary,
                a.Url,
                a.Source,
                a.SourceType,
                a.PublishedAt,
                a.Sentiment,
                a.Symbols.Select(s => s.Symbol).OrderBy(s => s).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<SentimentSummary>> GetSentimentSummaryAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        DateTimeOffset since = DateTimeOffset.UtcNow - window;

        var rows = await (
            from symbol in dbContext.Set<ArticleSymbol>().AsNoTracking()
            join article in dbContext.Articles.AsNoTracking() on symbol.ArticleId equals article.Id
            where article.PublishedAt >= since && article.Sentiment != null
            group article by symbol.Symbol into g
            select new { Symbol = g.Key, Count = g.Count(), Average = g.Average(a => a.Sentiment) })
            .ToListAsync(cancellationToken);

        Dictionary<string, string> names = await dbContext.Instruments
            .AsNoTracking()
            .ToDictionaryAsync(i => i.Symbol, i => i.Name, cancellationToken);

        return rows
            .Select(r => new SentimentSummary(r.Symbol, names.GetValueOrDefault(r.Symbol, r.Symbol), r.Count, r.Average ?? 0))
            .OrderByDescending(s => s.ArticleCount)
            .ToList();
    }
}
