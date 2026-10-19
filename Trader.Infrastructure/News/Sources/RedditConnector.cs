using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class RedditConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<RedditConnector> logger) : ISourceConnector
{
    private readonly RedditOptions _options = options.Value.Reddit;

    public string Name => "Reddit";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ClientId)
        && !string.IsNullOrWhiteSpace(_options.ClientSecret)
        && _options.Subreddits.Count > 0;

    public string? DisabledReason => IsEnabled ? null : "Faltam as credenciais da app Reddit (Sources:Reddit:ClientId e ClientSecret).";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        List<RawArticle> articles = [];

        try
        {
            string token = await GetTokenAsync(cancellationToken);

            foreach (string subreddit in _options.Subreddits)
            {
                using HttpRequestMessage request = new(HttpMethod.Get, $"https://oauth.reddit.com/r/{subreddit}/new?limit=25");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                articles.AddRange(Parse(await response.Content.ReadAsStringAsync(cancellationToken), subreddit));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Reddit failed: {Message}", ex.Message);
        }

        return articles;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "https://www.reddit.com/api/v1/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new HttpRequestException("Reddit returned no access token.");
    }

    internal static IReadOnlyList<RawArticle> Parse(string json, string subreddit)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<RawArticle> articles = [];

        foreach (JsonElement child in document.RootElement.GetProperty("data").GetProperty("children").EnumerateArray())
        {
            JsonElement data = child.GetProperty("data");
            string title = TextCleaner.Clean(data.GetProperty("title").GetString(), 300);

            if (string.IsNullOrEmpty(title))
            {
                continue;
            }

            articles.Add(new RawArticle(
                $"https://www.reddit.com{data.GetProperty("permalink").GetString()}",
                title,
                TextCleaner.Clean(data.TryGetProperty("selftext", out JsonElement text) ? text.GetString() : null, 500),
                $"r/{subreddit}",
                SourceType.Social,
                DateTimeOffset.FromUnixTimeSeconds((long)data.GetProperty("created_utc").GetDouble())));
        }

        return articles;
    }
}
