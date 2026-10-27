using Trader.Core.Models;

namespace Trader.Core.Intraday;

public enum RadarEventKind
{
    VolumeSpike,
    Gap,
    OpeningRangeBreakout,
    PreviousDayBreakout,
    MomentumBurst
}

public sealed record RadarEvent(
    string Symbol,
    string Name,
    RadarEventKind Kind,
    TradeDirection Direction,
    double Strength,
    string Detail,
    double Price,
    DateTimeOffset Time,
    string? Why = null);

public sealed record RadarRow(
    string Symbol,
    string Name,
    AssetClass AssetClass,
    double Price,
    double? DayChangePercent,
    double? GapPercent,
    double? RelativeVolume,
    double? VwapDistancePercent,
    DateTimeOffset LastCandleTime);

public sealed record RadarSnapshot(
    DateTimeOffset UpdatedAt,
    int ScannedCount,
    IReadOnlyList<RadarEvent> Events,
    IReadOnlyList<RadarRow> Board);

public sealed record IntradayContext(
    string Symbol,
    string Name,
    AssetClass AssetClass,
    double Price,
    double Atr,
    double? Vwap,
    double? RelativeVolume,
    double? GapPercent,
    double? OpeningRangeHigh,
    double? OpeningRangeLow,
    double? PreviousDayHigh,
    double? PreviousDayLow,
    double? PreviousDayClose,
    double DayOpen,
    double DayHigh,
    double DayLow,
    double RecentMove,
    DateTimeOffset SessionStart,
    DateTimeOffset LastCandleTime,
    IReadOnlyList<Candle> SessionCandles,
    DateTimeOffset? SessionEnd = null,
    double? MarketChangePercent = null,
    string? MacroEvent = null,
    double? Vix = null,
    double? VolatilityRatio = null);

public sealed class IntradayOptions
{
    public double VwapWeight { get; set; } = 1.0;
    public double OpeningRangeWeight { get; set; } = 1.0;
    public double VolumeWeight { get; set; } = 1.0;
    public double MomentumWeight { get; set; } = 1.0;
    public double PreviousDayWeight { get; set; } = 0.75;
    public double GapWeight { get; set; } = 0.75;
    public double NewsWeight { get; set; } = 1.25;
    public double CatalystWeight { get; set; } = 1.5;

    public double MinRelativeVolume { get; set; } = 1.2;
    public double VolumeSpikeRelativeVolume { get; set; } = 3;
    public double GapEventPercent { get; set; } = 2;
    public double BurstAtrMultiple { get; set; } = 2.5;
    public double BreakoutAtrBuffer { get; set; } = 0.1;

    public double SlAtrMultiple { get; set; } = 3.0;
    public double TpAtrMultiple { get; set; } = 5.4;
    public double MinRiskReward { get; set; } = 1.8;
    public double MinStopAtrMultiple { get; set; } = 2.0;
    public double ConfidenceThreshold { get; set; } = 0.7;
    public double ConfidenceSteepness { get; set; } = 2.5;
    public double MaxConfidence { get; set; } = 0.85;
    public double MinEntryMove { get; set; } = 0.75;
    public int MaxNewSuggestionsPerCycle { get; set; } = 5;
    public int MaxOpenSuggestions { get; set; } = 8;
    public int BreakoutFreshCandles { get; set; } = 12;
    public int MinAgreeingSignals { get; set; } = 3;

    public double NewsWindowHours { get; set; } = 6;
    public double NewsHalfLifeHours { get; set; } = 2;
    public double CatalystWindowMinutes { get; set; } = 120;
    public double CatalystMinSentiment { get; set; } = 0.4;
    public int ExpiryHours { get; set; } = 3;
    public int StaleAfterMinutes { get; set; } = 25;

    public int AvoidFirstMinutes { get; set; } = 15;
    public int AvoidLastMinutes { get; set; } = 30;
    public double MaxRecentMoveAtr { get; set; } = 4.5;
    public double MaxDayMovePercent { get; set; } = 8;

    public double MarketWeight { get; set; } = 0.75;
    public bool RequireMarketAlignment { get; set; }
    public double MarketAlignmentFloorPercent { get; set; } = 0.3;
    public double AvoidMacroHours { get; set; } = 2;

    public double MaxVix { get; set; } = 30;
    public double MaxVolatilityRatio { get; set; } = 2.5;

    public bool SmartExitEnabled { get; set; }
    public double PartialAtR { get; set; } = 1.0;
    public double PartialFraction { get; set; } = 0.5;
    public double BreakevenAtR { get; set; } = 1.0;
    public double LockR { get; set; } = 0.1;
    public double TrailAfterR { get; set; } = 1.5;
    public double TrailDistanceR { get; set; } = 1.0;

    public Trader.Core.Scoring.ExitPolicy ToExitPolicy() => new(SmartExitEnabled, PartialAtR, PartialFraction, BreakevenAtR, LockR, TrailAfterR, TrailDistanceR);

    public bool RecordShadow { get; set; } = true;
    public double ShadowMinConfidence { get; set; } = 0.5;
    public int MaxShadowPerCycle { get; set; } = 15;
}
