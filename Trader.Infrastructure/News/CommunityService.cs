using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.News.Sources;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.News;

public sealed class CommunityService(TraderDbContext dbContext, ApeWisdomClient apeWisdom, ISymbolProvider symbolProvider) : ICommunityService
{
    private const int Top = 50;

    public async Task<IReadOnlyList<CommunityTrend>> GetTrendingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ApeWisdomEntry> entries = await apeWisdom.GetTrendingAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return [];
        }

        HashSet<string> watchlist = (await symbolProvider.GetSymbolsAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset since = DateTimeOffset.UtcNow.AddHours(-24);

        var rows = await (
            from symbol in dbContext.Set<ArticleSymbol>().AsNoTracking()
            join article in dbContext.Articles.AsNoTracking() on symbol.ArticleId equals article.Id
            where article.PublishedAt >= since && article.SourceType == SourceType.Social
            group article by symbol.Symbol into g
            select new { Symbol = g.Key, Count = g.Count(), Average = g.Average(a => a.Sentiment) })
            .ToListAsync(cancellationToken);

        Dictionary<string, (int Count, double? Average)> posts = rows.ToDictionary(r => r.Symbol, r => (r.Count, r.Average), StringComparer.OrdinalIgnoreCase);

        return entries
            .Take(Top)
            .Select(e =>
            {
                (int count, double? average) = posts.GetValueOrDefault(e.Ticker);
                return new CommunityTrend(e.Rank, e.Ticker, e.Name, e.Mentions, e.MentionsPrevious, e.Upvotes, e.RankPrevious, watchlist.Contains(e.Ticker), count, average);
            })
            .ToList();
    }
}
