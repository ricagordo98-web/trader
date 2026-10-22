using Trader.Core.Models;

namespace Trader.Core.Explain;

public enum CauseCategory
{
    Earnings,
    Guidance,
    Analyst,
    MergerAcquisition,
    Management,
    Legal,
    Product,
    Dividend,
    Macro,
    Insider,
    Filing,
    Other
}

public enum EvidenceKind
{
    Article,
    Earnings,
    Filing,
    Macro
}

public enum MoveScope
{
    Small,
    MarketWide,
    Sector,
    CompanySpecific,
    Mixed
}

public sealed record MoveRequest(string Symbol, DateTimeOffset? From, DateTimeOffset? To, bool Refresh = false, bool Narrate = true);

public sealed record EvidenceQuery(string Symbol, string Name, AssetClass AssetClass, DateTimeOffset From, DateTimeOffset To);

public sealed record EvidenceItem(
    EvidenceKind Kind,
    CauseCategory Category,
    string Title,
    string Detail,
    string Url,
    string Source,
    DateTimeOffset Time,
    double Reliability,
    double? Sentiment,
    bool MentionsSymbol);

public sealed record PeerMove(string Symbol, string Name, double ChangePercent, double Correlation);

public sealed record BenchmarkMove(string Symbol, string Name, double ChangePercent, double Beta, double ExpectedPercent);

public sealed record MoveAnalysis(
    string Symbol,
    string Name,
    DateTimeOffset From,
    DateTimeOffset To,
    string Interval,
    double StartPrice,
    double EndPrice,
    double ChangePercent,
    double TypicalMovePercent,
    double ZScore,
    DateTimeOffset PeakTime,
    double PeakChangePercent,
    double PeakShare,
    double? VolumeRatio,
    double? GapPercent,
    BenchmarkMove? Benchmark,
    double ResidualPercent,
    IReadOnlyList<PeerMove> Peers,
    MoveScope Scope);

public sealed record MoveCause(
    string Id,
    CauseCategory Category,
    EvidenceKind Kind,
    string Title,
    string Detail,
    string Url,
    string Source,
    DateTimeOffset Time,
    double Score,
    string Confidence,
    int LeadMinutes,
    int Duplicates,
    double? Sentiment = null);

public static class NarrativeStatus
{
    public const string Disabled = "disabled";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Unavailable = "unavailable";
}

public sealed record MoveExplanation(
    MoveAnalysis Analysis,
    IReadOnlyList<MoveCause> Causes,
    string Summary,
    string? Narrative,
    string NarrativeStatus,
    string? NarrativeSource,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);

public sealed record NarrationResult(string? Text, string Status, string? Source, IReadOnlyList<string> Violations);
