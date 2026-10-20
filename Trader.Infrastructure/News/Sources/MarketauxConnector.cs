using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class MarketauxConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<MarketauxConnector> logger) : ISourceConnector
{
    private readonly MarketauxOptions _options = options.Value.Marketaux;

    public string Name => "Marketaux";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string? DisabledReason => IsEnabled ? null : "Falta a chave da API (Sources:Marketaux:ApiKey).";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            string after = DateTime.UtcNow.AddMinutes(-(_options.IntervalMinutes * 2)).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            string json = await httpClient.GetStringAsync(
                $"https://api.marketaux.com/v1/news/all?language=en&filter_entities=true&published_after={after}&api_token={Uri.EscapeDataString(_options.ApiKey)}",
                cancellationToken);

            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Marketaux failed: {Message}", ex.Message);
            return [];
        }
    }

    internal static IReadOnlyList<RawArticle> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Marketaux returned no data");
        }

        List<RawArticle> articles = [];

        foreach (JsonElement item in data.EnumerateArray())
        {
            string? url = item.TryGetProperty("url", out JsonElement u) ? u.GetString() : null;
            string title = TextCleaner.Clean(item.TryGetProperty("title", out JsonElement t) ? t.GetString() : null, 300);

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title)
                || !DateTimeOffset.TryParse(
                    item.TryGetProperty("published_at", out JsonElement p) ? p.GetString() : null,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset published))
            {
                continue;
            }

            List<string> symbols = item.TryGetProperty("entities", out JsonElement entities) && entities.ValueKind == JsonValueKind.Array
                ? entities.EnumerateArray()
                    .Select(e => e.TryGetProperty("symbol", out JsonElement s) ? s.GetString() : null)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s!)
                    .ToList()
                : [];

            articles.Add(new RawArticle(
                url,
                title,
                TextCleaner.Clean(item.TryGetProperty("description", out JsonElement d) ? d.GetString() : null, 500),
                item.TryGetProperty("source", out JsonElement src) ? src.GetString() ?? "Marketaux" : "Marketaux",
                SourceType.News,
                published,
                symbols));
        }

        return articles;
    }
}
