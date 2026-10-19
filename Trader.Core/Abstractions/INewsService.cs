using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface INewsService
{
    Task<IReadOnlyList<ArticleDto>> GetArticlesAsync(NewsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SentimentSummary>> GetSentimentSummaryAsync(TimeSpan window, CancellationToken cancellationToken);
}
