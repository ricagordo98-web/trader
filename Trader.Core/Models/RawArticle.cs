namespace Trader.Core.Models;

public sealed record RawArticle(
    string Url,
    string Title,
    string Summary,
    string Source,
    SourceType SourceType,
    DateTimeOffset PublishedAt,
    IReadOnlyList<string>? Symbols = null);
