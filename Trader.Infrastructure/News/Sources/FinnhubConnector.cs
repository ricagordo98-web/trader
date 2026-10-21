using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class FinnhubConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<FinnhubConnector> logger) : ISourceConnector
{
    private static readonly string[] Categories = ["general", "forex", "crypto", "merger"];

    private readonly FinnhubOptions _options = options.Value.Finnhub;

    public string Name => "Finnhub";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string? DisabledReason => IsEnabled ? null : "Falta a chave da API (Sources:Finnhub:ApiKey).";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        List<RawArticle> articles = [];

        foreach (string category in Categories)
        {
            try
            {
                string json = await httpClient.GetStringAsync(
                    $"https://finnhub.io/api/v1/news?category={category}&token={Uri.EscapeDataString(_options.ApiKey)}",
                    cancellationToken);
                articles.AddRange(Parse(json));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Finnhub category {Category} failed: {Message}", category, ex.Message);
            }
        }

        return articles;
    }

    internal static IReadOnlyList<RawArticle> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<RawArticle> articles = [];

        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            string? url = item.GetProperty("url").GetString();
            string title = TextCleaner.Clean(item.GetProperty("headline").GetString(), 300);

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            string source = item.TryGetProperty("source", out JsonElement s) ? s.GetString() ?? "Finnhub" : "Finnhub";
            string related = item.TryGetProperty("related", out JsonElement r) ? r.GetString() ?? string.Empty : string.Empty;

            articles.Add(new RawArticle(
                url,
                title,
                TextCleaner.Clean(item.GetProperty("summary").GetString(), 500),
                source,
                SourceType.News,
                DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("datetime").GetInt64()),
                related.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }

        return articles;
    }
}
