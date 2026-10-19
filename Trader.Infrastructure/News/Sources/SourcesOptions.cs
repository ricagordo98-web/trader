using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class SourcesOptions
{
    public const string SectionName = "Sources";

    public RssOptions Rss { get; set; } = new();
    public GdeltOptions Gdelt { get; set; } = new();
    public StockTwitsOptions StockTwits { get; set; } = new();
    public FinnhubOptions Finnhub { get; set; } = new();
    public RedditOptions Reddit { get; set; } = new();
    public EdgarOptions Edgar { get; set; } = new();
}

public sealed class RssOptions
{
    public int IntervalMinutes { get; set; } = 10;
    public string SymbolFeedTemplate { get; set; } = string.Empty;
    public string SymbolFeedName { get; set; } = "Yahoo Finance";
    public List<RssFeedOptions> Feeds { get; set; } = [];
}

public sealed class RssFeedOptions
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public SourceType SourceType { get; set; } = SourceType.News;
}

public sealed class GdeltOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 30;
    public List<string> Queries { get; set; } = [];
}

public sealed class StockTwitsOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
}

public sealed class FinnhubOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 10;
}

public sealed class RedditOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 10;
    public List<string> Subreddits { get; set; } = [];
}

public sealed class EdgarOptions
{
    public string ContactEmail { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 15;
}
