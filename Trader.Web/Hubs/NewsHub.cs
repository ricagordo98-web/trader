using Microsoft.AspNetCore.SignalR;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Hubs;

public sealed class NewsHub : Hub
{
    public const string ArticlesAddedMethod = "ArticlesAdded";

    public const string AlertsRaisedMethod = "AlertsRaised";
}

public sealed class SignalRNewsNotifier(IHubContext<NewsHub> hubContext) : INewsNotifier
{
    public Task NotifyArticlesAddedAsync(IReadOnlyList<ArticleDto> articles, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync(NewsHub.ArticlesAddedMethod, articles, cancellationToken);
}

public sealed class SignalRAlertNotifier(IHubContext<NewsHub> hubContext) : IAlertNotifier
{
    public Task NotifyAlertsAsync(IReadOnlyList<Trader.Core.Alerts.AlertDto> alerts, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync(NewsHub.AlertsRaisedMethod, alerts, cancellationToken);
}
