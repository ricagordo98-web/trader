namespace Trader.Core.Models;

public sealed record SentimentSummary(
    string Symbol,
    string Name,
    int ArticleCount,
    double AverageSentiment);
