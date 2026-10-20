using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.News;

public sealed class ArticleIngestor(
    TraderDbContext dbContext,
    ISentimentAnalyzer sentimentAnalyzer,
    ITickerExtractor tickerExtractor,
    INewsNotifier notifier,
    ILogger<ArticleIngestor> logger)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<int> IngestAsync(IReadOnlyList<RawArticle> rawArticles, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);

        try
        {
            return await IngestCoreAsync(rawArticles, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<int> IngestCoreAsync(IReadOnlyList<RawArticle> rawArticles, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<(RawArticle Raw, string UrlHash, string TitleHash)> candidates = rawArticles
            .Where(r => now - r.PublishedAt <= MaxAge)
            .Select(r => (Raw: r, UrlHash: ArticleHasher.HashUrl(r.Url), TitleHash: ArticleHasher.HashTitle(r.Title)))
            .GroupBy(c => c.UrlHash)
            .Select(g => g.First())
            .GroupBy(c => c.TitleHash)
            .Select(g => g.First())
            .ToList();

        if (candidates.Count == 0)
        {
            return 0;
        }

        string[] urlHashes = candidates.Select(c => c.UrlHash).ToArray();
        string[] titleHashes = candidates.Select(c => c.TitleHash).ToArray();

        HashSet<string> knownUrls = (await dbContext.Articles
            .Where(a => urlHashes.Contains(a.UrlHash))
            .Select(a => a.UrlHash)
            .ToListAsync(cancellationToken)).ToHashSet();

        HashSet<string> knownTitles = (await dbContext.Articles
            .Where(a => titleHashes.Contains(a.TitleHash))
            .Select(a => a.TitleHash)
            .ToListAsync(cancellationToken)).ToHashSet();

        HashSet<string> knownSymbols = (await dbContext.Instruments
            .Select(i => i.Symbol)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Regex symbolMatcher = DynamicSymbolMatcher.Build(knownSymbols);

        List<(RawArticle Raw, string UrlHash, string TitleHash)> fresh = candidates
            .Where(c => !knownUrls.Contains(c.UrlHash) && !knownTitles.Contains(c.TitleHash))
            .ToList();

        if (fresh.Count == 0)
        {
            return 0;
        }

        List<Article> articles = await Task.Run(
            () => fresh.Select(c => BuildArticle(c.Raw, c.UrlHash, c.TitleHash, now, knownSymbols, symbolMatcher)).ToList(),
            cancellationToken);

        dbContext.Articles.AddRange(articles);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ingested {Count} new articles", articles.Count);

        await notifier.NotifyArticlesAddedAsync(
            articles.Select(a => new ArticleDto(
                a.Id,
                a.Title,
                a.Summary,
                a.Url,
                a.Source,
                a.SourceType,
                a.PublishedAt,
                a.Sentiment,
                a.Symbols.Select(s => s.Symbol).ToList())).ToList(),
            cancellationToken);

        return articles.Count;
    }

    private Article BuildArticle(RawArticle raw, string urlHash, string titleHash, DateTimeOffset now, HashSet<string> knownSymbols, Regex symbolMatcher)
    {
        string text = string.IsNullOrEmpty(raw.Summary) ? raw.Title : $"{raw.Title}. {raw.Summary}";

        IEnumerable<string> symbols = (raw.Symbols ?? [])
            .Concat(tickerExtractor.Extract(text))
            .Concat(DynamicSymbolMatcher.Match(symbolMatcher, text))
            .Select(s => s.ToUpperInvariant())
            .Where(knownSymbols.Contains)
            .Distinct();

        return new Article
        {
            Url = raw.Url,
            UrlHash = urlHash,
            TitleHash = titleHash,
            Title = raw.Title,
            Summary = ArticleHasher.IsRedundantSummary(raw.Title, raw.Summary) ? string.Empty : raw.Summary,
            Source = raw.Source,
            SourceType = raw.SourceType,
            PublishedAt = raw.PublishedAt > now ? now : raw.PublishedAt,
            IngestedAt = now,
            Sentiment = sentimentAnalyzer.Analyze(text),
            Symbols = symbols.Select(s => new ArticleSymbol { Symbol = s }).ToList()
        };
    }
}
