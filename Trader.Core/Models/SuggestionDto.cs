using Trader.Core.Scoring;

namespace Trader.Core.Models;

public sealed record SuggestionEvidence(
    IReadOnlyList<SignalResult> Signals,
    IReadOnlyList<EvidenceArticle> Articles);

public sealed record SuggestionDto(
    long Id,
    string Symbol,
    string Name,
    TradeDirection Direction,
    double Confidence,
    double Entry,
    double StopLoss,
    double TakeProfit,
    double RiskReward,
    string Explanation,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    SuggestionStatus Status,
    DateTimeOffset? ClosedAt,
    double? ExitPrice,
    double? RMultiple,
    IReadOnlyList<SignalResult> Signals,
    IReadOnlyList<EvidenceArticle> Articles);

public sealed record CurvePoint(DateTimeOffset Time, double CumulativeR);

public sealed record AccuracyStat(string Name, string Label, int Count, double WinRate, double AverageR = 0);

public sealed record CalibrationBucket(string Label, int Count, double AverageConfidence, double WinRate, double AverageR);

public sealed record TrackRecordDto(
    int ClosedCount,
    int OpenCount,
    int Wins,
    double WinRate,
    double AverageR,
    double TotalR,
    double? ProfitFactor,
    IReadOnlyList<CurvePoint> Curve,
    IReadOnlyList<AccuracyStat> BySignal,
    IReadOnlyList<AccuracyStat> BySource,
    IReadOnlyList<CalibrationBucket> Calibration);
