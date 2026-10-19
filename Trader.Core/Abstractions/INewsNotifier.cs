using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface INewsNotifier
{
    Task NotifyArticlesAddedAsync(IReadOnlyList<ArticleDto> articles, CancellationToken cancellationToken);
}
