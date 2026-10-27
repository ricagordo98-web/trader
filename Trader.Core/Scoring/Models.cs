using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed record MarketSnapshot(
    string Symbol,
    string Name,
    double Price,
    double Atr,
    double Ema20,
    double Ema50,
    double? Ema200,
    double Rsi,
    double MacdHistogram,
    double PercentB,
    double SwingLow,
    double SwingHigh,
    double? Adx = null,
    double? WeeklyTrend = null,
    double? RelativeStrength = null,
    int? DaysToEarnings = null,
    double? Vix = null);

public sealed record SentimentSnapshot(
    double WeightedSentiment,
    int ArticleCount,
    double? BuzzZScore,
    IReadOnlyList<EvidenceArticle> Articles);

public sealed record SignalResult(
    string Name,
    string Label,
    double Value,
    double Weight,
    string Detail);

public sealed record EvidenceArticle(
    string Title,
    string Url,
    string Source,
    double Sentiment,
    DateTimeOffset PublishedAt,
    double Influence);

public sealed record SuggestionDraft(
    string Symbol,
    string Name,
    TradeDirection Direction,
    double Confidence,
    double Score,
    double Entry,
    double StopLoss,
    double TakeProfit,
    double RiskReward,
    IReadOnlyList<SignalResult> Signals,
    IReadOnlyList<EvidenceArticle> Articles,
    string Explanation,
    IReadOnlyList<string>? Blockers = null)
{
    public IReadOnlyList<string> Reasons => Blockers ?? [];

    public bool IsShadow => Reasons.Count > 0;
}
