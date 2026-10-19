using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class RssConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ISymbolProvider symbolProvider, ILogger<RssConnector> logger) : ISourceConnector
{
    private readonly RssOptions _options = options.Value.Rss;

    public string Name => "RSS";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => _options.Feeds.Count > 0 || !string.IsNullOrWhiteSpace(_options.SymbolFeedTemplate);

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        using SemaphoreSlim throttle = new(4);

        Task<IReadOnlyList<RawArticle>>[] tasks = (await BuildFeedsAsync(cancellationToken))
            .Select(async feed =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    return await FetchFeedAsync(feed, cancellationToken);
                }
                finally
                {
                    throttle.Release();
                }
            })
            .ToArray();

        return (await Task.WhenAll(tasks)).SelectMany(a => a).ToList();
    }

    private async Task<IReadOnlyList<RssFeedOptions>> BuildFeedsAsync(CancellationToken cancellationToken)
    {
        List<RssFeedOptions> feeds = [.. _options.Feeds];

        if (!string.IsNullOrWhiteSpace(_options.SymbolFeedTemplate))
        {
            foreach (string symbol in await symbolProvider.GetSymbolsAsync(cancellationToken))
            {
                feeds.Add(new RssFeedOptions
                {
                    Name = _options.SymbolFeedName,
                    Symbol = symbol,
                    Url = _options.SymbolFeedTemplate.Replace("{symbol}", Uri.EscapeDataString(symbol).Replace("%3D", "=", StringComparison.Ordinal), StringComparison.Ordinal)
                });
            }
        }

        return feeds;
    }

    private async Task<IReadOnlyList<RawArticle>> FetchFeedAsync(RssFeedOptions feed, CancellationToken cancellationToken)
    {
        try
        {
            await using Stream stream = await httpClient.GetStreamAsync(feed.Url, cancellationToken);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            SyndicationFeed syndication = SyndicationFeed.Load(reader);

            return syndication.Items
                .Select(item => ToArticle(feed, item))
                .OfType<RawArticle>()
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("RSS feed {Feed} failed: {Message}", feed.Name, ex.Message);
            return [];
        }
    }

    private static RawArticle? ToArticle(RssFeedOptions feed, SyndicationItem item)
    {
        string? url = item.Links.FirstOrDefault()?.Uri?.ToString();
        string title = TextCleaner.Clean(item.Title?.Text, 300);

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title))
        {
            return null;
        }

        DateTimeOffset published = item.PublishDate != default ? item.PublishDate : item.LastUpdatedTime;
        if (published == default)
        {
            published = DateTimeOffset.UtcNow;
        }

        return new RawArticle(
            url,
            title,
            TextCleaner.Clean(item.Summary?.Text, 500),
            feed.Name,
            feed.SourceType,
            published,
            feed.Symbol is null ? null : [feed.Symbol]);
    }
}
