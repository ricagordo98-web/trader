namespace Trader.Core.Models;

public sealed class Article
{
    public long Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string UrlHash { get; set; } = string.Empty;
    public string TitleHash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public SourceType SourceType { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public double? Sentiment { get; set; }
    public List<ArticleSymbol> Symbols { get; set; } = [];
}

public sealed class ArticleSymbol
{
    public long ArticleId { get; set; }
    public string Symbol { get; set; } = string.Empty;
}
