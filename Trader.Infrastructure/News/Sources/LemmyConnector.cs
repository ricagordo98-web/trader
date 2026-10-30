using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class LemmyConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<LemmyConnector> logger) : ISourceConnector
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly LemmyOptions _options = options.Value.Lemmy;

    public string Name => "Lemmy";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => _options.Enabled && _options.Communities.Count > 0;

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        List<RawArticle> articles = [];

        foreach (string community in _options.Communities)
        {
            string[] parts = community.Split('@', 2);

            if (parts.Length != 2)
            {
                continue;
            }

            try
            {
                string json = await httpClient.GetStringAsync(
                    $"https://{parts[1]}/api/v3/post/list?community_name={Uri.EscapeDataString(parts[0])}&sort=New&limit=30",
                    cancellationToken);
                articles.AddRange(Parse(json, DateTimeOffset.UtcNow));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Lemmy {Community} failed: {Message}", community, ex.Message);
            }
        }

        return articles;
    }

    internal static IReadOnlyList<RawArticle> Parse(string json, DateTimeOffset now)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<RawArticle> articles = [];

        foreach (JsonElement entry in document.RootElement.GetProperty("posts").EnumerateArray())
        {
            JsonElement post = entry.GetProperty("post");
            string title = TextCleaner.Clean(post.GetProperty("name").GetString(), 300);
            DateTimeOffset published = post.GetProperty("published").GetDateTimeOffset();
            string? link = post.TryGetProperty("url", out JsonElement url) ? url.GetString() : null;
            string? discussion = post.GetProperty("ap_id").GetString();

            if (string.IsNullOrEmpty(title)
                || now - published > MaxAge
                || post.GetProperty("removed").GetBoolean()
                || post.GetProperty("deleted").GetBoolean()
                || post.GetProperty("nsfw").GetBoolean())
            {
                continue;
            }

            string community = entry.GetProperty("community").GetProperty("name").GetString() ?? "lemmy";
            string summary = TextCleaner.Clean(post.TryGetProperty("body", out JsonElement body) ? body.GetString() : null, 500);

            articles.Add(new RawArticle(string.IsNullOrEmpty(link) ? discussion ?? string.Empty : link, title, summary, $"Lemmy c/{community}", SourceType.Social, published));
        }

        return articles.Where(a => a.Url.Length > 0).ToList();
    }
}
