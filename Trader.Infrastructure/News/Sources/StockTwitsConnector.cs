using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class StockTwitsConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<StockTwitsConnector> logger) : ISourceConnector
{
    private readonly StockTwitsOptions _options = options.Value.StockTwits;

    public string Name => "StockTwits";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => _options.Enabled;

    public string? DisabledReason => IsEnabled ? null : "Desligada: o serviço rejeita clientes que não sejam browsers (HTTP 403).";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await httpClient.GetStringAsync("https://api.stocktwits.com/api/2/streams/trending.json", cancellationToken);
            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("StockTwits failed: {Message}", ex.Message);
            return [];
        }
    }

    internal static IReadOnlyList<RawArticle> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<RawArticle> articles = [];

        foreach (JsonElement message in document.RootElement.GetProperty("messages").EnumerateArray())
        {
            string body = TextCleaner.Clean(message.GetProperty("body").GetString(), 400);
            string username = message.GetProperty("user").GetProperty("username").GetString() ?? "user";
            long id = message.GetProperty("id").GetInt64();

            if (string.IsNullOrEmpty(body))
            {
                continue;
            }

            string[] symbols = message.TryGetProperty("symbols", out JsonElement list)
                ? list.EnumerateArray().Select(s => s.GetProperty("symbol").GetString()).OfType<string>().ToArray()
                : [];

            articles.Add(new RawArticle(
                $"https://stocktwits.com/{username}/message/{id}",
                body,
                string.Empty,
                "StockTwits",
                SourceType.Social,
                message.GetProperty("created_at").GetDateTimeOffset(),
                symbols));
        }

        return articles;
    }
}
