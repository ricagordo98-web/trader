using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Hubs;

public sealed record LiveSubscription(string Symbol, string Interval, string Range)
{
    public string Group => $"live:{Symbol}:{Interval}:{Range}";
}

public sealed class LiveSubscriptions
{
    private readonly ConcurrentDictionary<string, (LiveSubscription Subscription, HashSet<string> Connections)> _groups = new();

    public void Add(string connectionId, LiveSubscription subscription)
    {
        (LiveSubscription Subscription, HashSet<string> Connections) entry = _groups.GetOrAdd(subscription.Group, _ => (subscription, []));

        lock (entry.Connections)
        {
            entry.Connections.Add(connectionId);
        }
    }

    public void Remove(string connectionId, LiveSubscription subscription)
    {
        if (!_groups.TryGetValue(subscription.Group, out (LiveSubscription Subscription, HashSet<string> Connections) entry))
        {
            return;
        }

        lock (entry.Connections)
        {
            entry.Connections.Remove(connectionId);
        }
    }

    public IReadOnlyList<string> RemoveConnection(string connectionId)
    {
        List<string> groups = [];

        foreach ((string group, (LiveSubscription Subscription, HashSet<string> Connections) entry) in _groups)
        {
            lock (entry.Connections)
            {
                if (entry.Connections.Remove(connectionId))
                {
                    groups.Add(group);
                }
            }
        }

        return groups;
    }

    public IReadOnlyList<LiveSubscription> Active()
    {
        List<LiveSubscription> active = [];

        foreach ((LiveSubscription subscription, HashSet<string> connections) in _groups.Values)
        {
            lock (connections)
            {
                if (connections.Count > 0)
                {
                    active.Add(subscription);
                }
            }
        }

        return active;
    }
}

public sealed class LiveCandlePublisher(
    LiveSubscriptions subscriptions,
    IHubContext<NewsHub> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<LiveCandlePublisher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<string, IReadOnlyList<Candle>> _lastSent = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PublishAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public static bool HasChanged(IReadOnlyList<Candle>? previous, IReadOnlyList<Candle> current) =>
        previous is null || !previous.SequenceEqual(current);

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveSubscription> active = subscriptions.Active();

        foreach (string stale in _lastSent.Keys.Except(active.Select(s => s.Group)).ToList())
        {
            _lastSent.Remove(stale);
        }

        if (active.Count == 0)
        {
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IMarketDataService marketData = scope.ServiceProvider.GetRequiredService<IMarketDataService>();

        foreach (LiveSubscription subscription in active)
        {
            try
            {
                IReadOnlyList<Candle> candles = await marketData.GetLiveCandlesAsync(subscription.Symbol, subscription.Interval, subscription.Range, cancellationToken);
                _lastSent.TryGetValue(subscription.Group, out IReadOnlyList<Candle>? previous);

                if (candles.Count == 0 || !HasChanged(previous, candles))
                {
                    continue;
                }

                _lastSent[subscription.Group] = candles;
                await hub.Clients.Group(subscription.Group).SendAsync(
                    NewsHub.LiveCandlesMethod,
                    new LiveCandles(subscription.Symbol, subscription.Interval, candles),
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogDebug("Live candles for {Symbol} unavailable: {Message}", subscription.Symbol, ex.Message);
            }
        }
    }
}
