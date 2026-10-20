using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class HackerNewsConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<HackerNewsConnector> logger) : ISourceConnector
{
    private const int StoriesToScan = 80;

    private static readonly Regex Keywords = new(
        @"\b(stocks?|markets?|nasdaq|s&p|dow jones|federal reserve|inflation|interest rates?|recession|oil|gold|bitcoin|earnings|ipo|tariffs?|econom\w*|banks?|currency|dollar|treasury|nvidia|apple|tesla|microsoft|amazon|google)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HackerNewsOptions _options = options.Value.HackerNews;

    public string Name => "Hacker News";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => _options.Enabled;

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            long[] ids = JsonSerializer.Deserialize<long[]>(
                await httpClient.GetStringAsync("https://hacker-news.firebaseio.com/v0/topstories.json", cancellationToken)) ?? [];

            using SemaphoreSlim gate = new(8);

            IEnumerable<Task<RawArticle?>> tasks = ids.Take(StoriesToScan).Select(async id =>
            {
                await gate.WaitAsync(cancellationToken);

                try
                {
                    return Parse(await httpClient.GetStringAsync($"https://hacker-news.firebaseio.com/v0/item/{id}.json", cancellationToken));
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    logger.LogDebug("Hacker News item {Id} failed: {Message}", id, ex.Message);
                    return null;
                }
                finally
                {
                    gate.Release();
                }
            });

            return (await Task.WhenAll(tasks)).OfType<RawArticle>().ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Hacker News failed: {Message}", ex.Message);
            return [];
        }
    }

    internal static RawArticle? Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement item = document.RootElement;

        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("title", out JsonElement titleElement)
            || !item.TryGetProperty("time", out JsonElement time))
        {
            return null;
        }

        string title = TextCleaner.Clean(titleElement.GetString(), 300);

        if (!Keywords.IsMatch(title))
        {
            return null;
        }

        long id = item.GetProperty("id").GetInt64();
        string discussion = $"https://news.ycombinator.com/item?id={id}";
        string url = item.TryGetProperty("url", out JsonElement u) && !string.IsNullOrEmpty(u.GetString()) ? u.GetString()! : discussion;
        int score = item.TryGetProperty("score", out JsonElement s) ? s.GetInt32() : 0;
        int comments = item.TryGetProperty("descendants", out JsonElement d) ? d.GetInt32() : 0;

        return new RawArticle(
            url,
            title,
            $"{score} pontos e {comments} comentários no Hacker News.",
            "Hacker News",
            SourceType.Social,
            DateTimeOffset.FromUnixTimeSeconds(time.GetInt64()));
    }
}
