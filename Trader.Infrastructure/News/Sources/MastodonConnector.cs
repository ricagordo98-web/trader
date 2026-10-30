using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class MastodonConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<MastodonConnector> logger) : ISourceConnector
{
    private const int MinLength = 40;

    private readonly MastodonOptions _options = options.Value.Mastodon;

    public string Name => "Mastodon";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => _options.Enabled && _options.Hashtags.Count > 0;

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        List<RawArticle> articles = [];

        foreach (string hashtag in _options.Hashtags)
        {
            try
            {
                string json = await httpClient.GetStringAsync($"{_options.Instance.TrimEnd('/')}/api/v1/timelines/tag/{Uri.EscapeDataString(hashtag)}?limit=40", cancellationToken);
                articles.AddRange(Parse(json));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Mastodon #{Hashtag} failed: {Message}", hashtag, ex.Message);
            }
        }

        return articles;
    }

    internal static IReadOnlyList<RawArticle> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<RawArticle> articles = [];

        foreach (JsonElement status in document.RootElement.EnumerateArray())
        {
            string? language = status.TryGetProperty("language", out JsonElement lang) ? lang.GetString() : null;
            string? spoiler = status.TryGetProperty("spoiler_text", out JsonElement spoilerText) ? spoilerText.GetString() : null;
            string text = TextCleaner.Clean(status.GetProperty("content").GetString(), 400);
            string? url = status.GetProperty("url").GetString();

            if ((language is not null && language != "en") || !string.IsNullOrEmpty(spoiler) || text.Length < MinLength || string.IsNullOrEmpty(url))
            {
                continue;
            }

            articles.Add(new RawArticle(url, text, string.Empty, "Mastodon", SourceType.Social, status.GetProperty("created_at").GetDateTimeOffset()));
        }

        return articles;
    }
}
