namespace Trader.Core.Models;

public enum TradeDirection
{
    Buy,
    Sell
}

public enum SuggestionStatus
{
    Open,
    HitTakeProfit,
    HitStopLoss,
    Expired
}

public sealed class Suggestion
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public double Confidence { get; set; }
    public double Score { get; set; }
    public double Entry { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double RiskReward { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public SuggestionStatus Status { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public double? ExitPrice { get; set; }
    public double? RMultiple { get; set; }
}
