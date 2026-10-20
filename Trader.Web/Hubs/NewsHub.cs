using Microsoft.AspNetCore.SignalR;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Hubs;

public sealed class NewsHub(LiveSubscriptions subscriptions) : Hub
{
    public const string ArticlesAddedMethod = "ArticlesAdded";

    public const string AlertsRaisedMethod = "AlertsRaised";

    public const string LiveCandlesMethod = "LiveCandles";

    public async Task SubscribeLive(string symbol, string interval, string range)
    {
        LiveSubscription subscription = Validate(symbol, interval, range);
        await Groups.AddToGroupAsync(Context.ConnectionId, subscription.Group);
        subscriptions.Add(Context.ConnectionId, subscription);
    }

    public async Task UnsubscribeLive(string symbol, string interval, string range)
    {
        LiveSubscription subscription = Validate(symbol, interval, range);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, subscription.Group);
        subscriptions.Remove(Context.ConnectionId, subscription);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static LiveSubscription Validate(string symbol, string interval, string range) =>
        string.IsNullOrWhiteSpace(symbol) || symbol.Length > 32 || !ChartParameters.IsValid(interval, range)
            ? throw new HubException("Invalid live subscription")
            : new LiveSubscription(symbol, interval, range);
}

public sealed class SignalRNewsNotifier(IHubContext<NewsHub> hubContext) : INewsNotifier
{
    public Task NotifyArticlesAddedAsync(IReadOnlyList<ArticleDto> articles, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync(NewsHub.ArticlesAddedMethod, articles, cancellationToken);
}

public sealed class SignalRAlertNotifier(IHubContext<NewsHub> hubContext) : IAlertNotifier
{
    public Task NotifyAlertsAsync(IReadOnlyList<Trader.Core.Alerts.AlertDto> alerts, string? userId, CancellationToken cancellationToken) =>
        (userId is null ? hubContext.Clients.All : hubContext.Clients.User(userId)).SendAsync(NewsHub.AlertsRaisedMethod, alerts, cancellationToken);
}
