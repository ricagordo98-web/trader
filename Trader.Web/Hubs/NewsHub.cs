using Microsoft.AspNetCore.SignalR;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Hubs;

public sealed class NewsHub : Hub
{
    public const string ArticlesAddedMethod = "ArticlesAdded";
}

public sealed class SignalRNewsNotifier(IHubContext<NewsHub> hubContext) : INewsNotifier
{
    public Task NotifyArticlesAddedAsync(IReadOnlyList<ArticleDto> articles, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync(NewsHub.ArticlesAddedMethod, articles, cancellationToken);
}
