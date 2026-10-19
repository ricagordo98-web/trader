namespace Trader.Core.Models;

public sealed record ArticleDto(
    long Id,
    string Title,
    string Summary,
    string Url,
    string Source,
    SourceType SourceType,
    DateTimeOffset PublishedAt,
    double? Sentiment,
    IReadOnlyList<string> Symbols);
