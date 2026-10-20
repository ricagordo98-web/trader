using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class AlphaVantageConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<AlphaVantageConnector> logger) : ISourceConnector
{
    private readonly AlphaVantageOptions _options = options.Value.AlphaVantage;

    public string Name => "Alpha Vantage";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string? DisabledReason => IsEnabled ? null : "Falta a chave da API (Sources:AlphaVantage:ApiKey).";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await httpClient.GetStringAsync(
                $"https://www.alphavantage.co/query?function=NEWS_SENTIMENT&topics=financial_markets,economy_macro,earnings&sort=LATEST&limit=200&apikey={Uri.EscapeDataString(_options.ApiKey)}",
                cancellationToken);

            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Alpha Vantage failed: {Message}", ex.Message);
            return [];
        }
    }

    internal static IReadOnlyList<RawArticle> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("feed", out JsonElement feed) || feed.ValueKind != JsonValueKind.Array)
        {
            string reason = document.RootElement.TryGetProperty("Information", out JsonElement info) ? info.GetString() ?? string.Empty
                : document.RootElement.TryGetProperty("Note", out JsonElement note) ? note.GetString() ?? string.Empty
                : "unexpected response";

            throw new JsonException($"Alpha Vantage returned no feed: {TextCleaner.Clean(reason, 160)}");
        }

        List<RawArticle> articles = [];

        foreach (JsonElement item in feed.EnumerateArray())
        {
            string? url = item.TryGetProperty("url", out JsonElement u) ? u.GetString() : null;
            string title = TextCleaner.Clean(item.TryGetProperty("title", out JsonElement t) ? t.GetString() : null, 300);

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title)
                || !DateTimeOffset.TryParseExact(
                    item.TryGetProperty("time_published", out JsonElement tp) ? tp.GetString() : null,
                    "yyyyMMdd'T'HHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset published))
            {
                continue;
            }

            List<string> symbols = item.TryGetProperty("ticker_sentiment", out JsonElement tickers) && tickers.ValueKind == JsonValueKind.Array
                ? tickers.EnumerateArray()
                    .Select(x => x.TryGetProperty("ticker", out JsonElement s) ? s.GetString() : null)
                    .Where(s => !string.IsNullOrEmpty(s) && !s.Contains(':'))
                    .Select(s => s!)
                    .ToList()
                : [];

            articles.Add(new RawArticle(
                url,
                title,
                TextCleaner.Clean(item.TryGetProperty("summary", out JsonElement sm) ? sm.GetString() : null, 500),
                item.TryGetProperty("source", out JsonElement src) ? src.GetString() ?? "Alpha Vantage" : "Alpha Vantage",
                SourceType.News,
                published,
                symbols));
        }

        return articles;
    }
}
