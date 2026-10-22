using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class EdgarConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<EdgarConnector> logger) : ISourceConnector
{
    private const string FeedUrl = "https://www.sec.gov/cgi-bin/browse-edgar?action=getcurrent&type=8-K&count=40&output=atom";

    private readonly EdgarOptions _options = options.Value.Edgar;

    public string Name => "SEC EDGAR";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ContactEmail);

    public string? DisabledReason => IsEnabled ? null : "Falta o email de contacto exigido pela SEC (Sources:Edgar:ContactEmail).";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, FeedUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", $"Trader personal research tool {_options.ContactEmail}");

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            SyndicationFeed feed = SyndicationFeed.Load(reader);

            return feed.Items
                .Select(item => new { item, url = item.Links.FirstOrDefault()?.Uri?.ToString() })
                .Where(x => x.url is not null)
                .Select(x => new RawArticle(
                    x.url!,
                    TextCleaner.Clean(x.item.Title?.Text, 300),
                    TextCleaner.Clean(x.item.Summary?.Text, 300),
                    "SEC EDGAR",
                    SourceType.Filing,
                    x.item.LastUpdatedTime != default ? x.item.LastUpdatedTime : DateTimeOffset.UtcNow))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("EDGAR failed: {Message}", ex.Message);
            return [];
        }
    }
}
