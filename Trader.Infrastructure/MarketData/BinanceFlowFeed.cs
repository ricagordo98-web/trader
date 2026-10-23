using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Core.Market;

namespace Trader.Infrastructure.MarketData;

public interface IOrderFlowService
{
    OrderFlowSnapshot Get(string symbol);
}

public sealed class BinanceFlowFeed(ILogger<BinanceFlowFeed> logger) : BackgroundService, IOrderFlowService
{
    private const string Endpoint = "wss://stream.binance.com:9443/stream";
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, FlowAggregator> _aggregators = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRequested = new();
    private readonly ConcurrentDictionary<string, string> _symbolByStream = new();
    private readonly HashSet<string> _subscribed = [];
    private volatile bool _connected;
    private string? _lastError;

    public OrderFlowSnapshot Get(string symbol)
    {
        string? stream = FlowSymbols.ToBinanceStream(symbol);

        if (stream is null)
        {
            return new OrderFlowSnapshot(symbol, false, "O fluxo de ordens só está disponível para cripto negociada na Binance.", null, null, 0.5, 0, 0, []);
        }

        _lastRequested[stream] = DateTimeOffset.UtcNow;
        _symbolByStream[stream] = symbol;
        FlowAggregator aggregator = _aggregators.GetOrAdd(stream, _ => new FlowAggregator());
        OrderFlowSnapshot snapshot = aggregator.Snapshot(symbol, DateTimeOffset.UtcNow);

        return snapshot.Available || _connected
            ? snapshot
            : snapshot with { Note = _lastError is null ? "A ligar à Binance…" : $"Binance indisponível ({_lastError}). Algumas regiões bloqueiam o acesso." };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_lastRequested.IsEmpty)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                await RunConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or JsonException)
            {
                _lastError = ex.Message.Length > 60 ? ex.Message[..60] : ex.Message;
                logger.LogDebug("Binance flow feed disconnected: {Message}", ex.Message);
            }

            _connected = false;
            _subscribed.Clear();

            try
            {
                await Task.Delay(Reconnect, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        using ClientWebSocket socket = new();
        await socket.ConnectAsync(new Uri(Endpoint), cancellationToken);
        _connected = true;
        _lastError = null;

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task maintenance = MaintainAsync(socket, linked.Token);

        try
        {
            await ReceiveAsync(socket, linked.Token);
        }
        finally
        {
            await linked.CancelAsync();

            try
            {
                await maintenance;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task MaintainAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string[] wanted = _lastRequested.Where(r => now - r.Value < IdleAfter).Select(r => r.Key).ToArray();
            string[] add = wanted.Where(w => !_subscribed.Contains(w)).ToArray();
            string[] remove = _subscribed.Where(s => !wanted.Contains(s)).ToArray();

            if (add.Length > 0)
            {
                await SendAsync(socket, "SUBSCRIBE", add, cancellationToken);
                _subscribed.UnionWith(add);
            }

            if (remove.Length > 0)
            {
                await SendAsync(socket, "UNSUBSCRIBE", remove, cancellationToken);
                _subscribed.ExceptWith(remove);
            }

            await Task.Delay(add.Length > 0 ? TimeSpan.FromSeconds(2) : MaintenanceInterval, cancellationToken);
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string method, IEnumerable<string> streams, CancellationToken cancellationToken)
    {
        string[] parameters = streams.SelectMany(s => new[] { $"{s}@aggTrade", $"{s}@depth5@1000ms" }).ToArray();
        string json = JsonSerializer.Serialize(new { method, @params = parameters, id = Environment.TickCount });
        return socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using MemoryStream message = new();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            Handle(Encoding.UTF8.GetString(message.ToArray()));
        }
    }

    internal void Handle(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("stream", out JsonElement streamElement) || !root.TryGetProperty("data", out JsonElement data))
        {
            return;
        }

        string name = streamElement.GetString() ?? string.Empty;
        int at = name.IndexOf('@');

        if (at < 0 || !_aggregators.TryGetValue(name[..at], out FlowAggregator? aggregator))
        {
            return;
        }

        if (name.Contains("aggTrade", StringComparison.Ordinal))
        {
            if (Number(data, "q") is { } quantity && data.TryGetProperty("m", out JsonElement maker) && data.TryGetProperty("T", out JsonElement time))
            {
                aggregator.AddTrade(DateTimeOffset.FromUnixTimeMilliseconds(time.GetInt64()), quantity, maker.GetBoolean());
            }
        }
        else if (name.Contains("depth", StringComparison.Ordinal))
        {
            aggregator.SetDepth(Levels(data, "bids"), Levels(data, "asks"), DateTimeOffset.UtcNow);
        }
    }

    private static List<double> Levels(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement levels) && levels.ValueKind == JsonValueKind.Array
            ? levels.EnumerateArray().Select(l => l.GetArrayLength() > 1 ? Parse(l[1].GetString()) : 0).ToList()
            : [];

    private static double? Number(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement element) ? Parse(element.GetString()) : null;

    private static double Parse(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
}
