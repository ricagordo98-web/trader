using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed partial class RedditFeedConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<RedditFeedConnector> logger) : ISourceConnector
{
    private readonly RedditOptions _options = options.Value.Reddit;

    public string Name => "Reddit (RSS)";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => string.IsNullOrWhiteSpace(_options.ClientId) && _options.Subreddits.Count > 0;

    public string? DisabledReason => IsEnabled ? null : "Desligada: com as credenciais da app Reddit configuradas, é o conector Reddit que recolhe estas comunidades.";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://www.reddit.com/r/{string.Join('+', _options.Subreddits)}/new/.rss?limit=100";
            await using Stream stream = await httpClient.GetStreamAsync(url, cancellationToken);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

            return Parse(SyndicationFeed.Load(reader));
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Reddit RSS failed: {Message}", ex.Message);
            return [];
        }
    }

    internal static IReadOnlyList<RawArticle> Parse(SyndicationFeed feed)
    {
        List<RawArticle> articles = [];

        foreach (SyndicationItem item in feed.Items)
        {
            string? url = item.Links.FirstOrDefault()?.Uri?.ToString();
            string title = TextCleaner.Clean(item.Title?.Text, 300);
            string? subreddit = item.Categories.FirstOrDefault()?.Name;

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(subreddit))
            {
                continue;
            }

            string body = TextCleaner.Clean((item.Content as TextSyndicationContent)?.Text, 3000);
            string summary = TextCleaner.Clean(Boilerplate().Replace(body, string.Empty), 500);
            DateTimeOffset published = item.PublishDate != default ? item.PublishDate : item.LastUpdatedTime;

            articles.Add(new RawArticle(url, title, summary, $"r/{subreddit}", SourceType.Social, published == default ? DateTimeOffset.UtcNow : published));
        }

        return articles;
    }

    [GeneratedRegex(@"\s*submitted by\s+/u/\S+\s+to\s+r/\S+(\s+\[link\])?(\s+\[comments\])?\s*$")]
    private static partial Regex Boilerplate();
}
