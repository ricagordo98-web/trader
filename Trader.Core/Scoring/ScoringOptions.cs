namespace Trader.Core.Scoring;

public sealed class ScoringOptions
{
    public const string SectionName = "Scoring";

    public double SentimentWeight { get; set; } = 1.5;
    public double BuzzWeight { get; set; } = 0.5;
    public double TrendWeight { get; set; } = 1.0;
    public double MomentumWeight { get; set; } = 1.0;
    public double BollingerWeight { get; set; } = 0.5;

    public double ConfidenceThreshold { get; set; } = 0.6;
    public double ConfidenceSteepness { get; set; } = 4;
    public int MinAgreeingSignals { get; set; } = 3;

    public int MinSentimentArticles { get; set; } = 3;
    public double SentimentScale { get; set; } = 0.5;
    public double SentimentHalfLifeHours { get; set; } = 12;

    public double SlAtrMultiple { get; set; } = 2.0;
    public double TpAtrMultiple { get; set; } = 4.0;
    public double MinRiskReward { get; set; } = 1.8;
    public int ExpiryDays { get; set; } = 5;

    public bool RecordShadow { get; set; } = true;
    public double ShadowMinConfidence { get; set; } = 0.45;
}
