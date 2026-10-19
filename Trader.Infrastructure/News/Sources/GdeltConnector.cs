using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class GdeltConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<GdeltConnector> logger) : ISourceConnector
{
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(5.5);

    private readonly GdeltOptions _options = options.Value.Gdelt;

    public string Name => "GDELT";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => _options.Enabled && _options.Queries.Count > 0;

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        List<RawArticle> articles = [];

        foreach (string query in _options.Queries)
        {
            try
            {
                string search = Uri.EscapeDataString($"\"{query}\" sourcelang:english");
                string url = $"https://api.gdeltproject.org/api/v2/doc/doc?query={search}&mode=artlist&format=json&maxrecords=25&timespan=2h&sort=datedesc";
                string json = await httpClient.GetStringAsync(url, cancellationToken);
                articles.AddRange(Parse(json));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("GDELT rate limit reached; skipping the remaining queries this cycle");
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("GDELT query {Query} failed: {Message}", query, ex.Message);
            }

            await Task.Delay(RequestSpacing, cancellationToken);
        }

        return articles;
    }

    internal static IReadOnlyList<RawArticle> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("articles", out JsonElement items))
        {
            return [];
        }

        List<RawArticle> articles = [];

        foreach (JsonElement item in items.EnumerateArray())
        {
            string? url = item.GetProperty("url").GetString();
            string title = TextCleaner.Clean(item.GetProperty("title").GetString(), 300);

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            DateTimeOffset published = DateTimeOffset.TryParseExact(
                item.GetProperty("seendate").GetString(),
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            string domain = item.TryGetProperty("domain", out JsonElement d) ? d.GetString() ?? "GDELT" : "GDELT";
            articles.Add(new RawArticle(url, title, string.Empty, domain, SourceType.News, published));
        }

        return articles;
    }
}
