namespace Trader.Core.Models;

public sealed record NewsQuery(
    string? Symbol = null,
    SourceType? SourceType = null,
    string? Sentiment = null,
    string? Search = null,
    DateTimeOffset? Before = null,
    int Limit = 50);
