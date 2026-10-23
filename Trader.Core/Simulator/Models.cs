using Trader.Core.Models;

namespace Trader.Core.Simulator;

public enum PaperStatus
{
    Open,
    HitTakeProfit,
    HitStopLoss,
    ClosedManually
}

public sealed class PaperTrade
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public double Quantity { get; set; }
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public PaperStatus Status { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public double? ExitPrice { get; set; }
    public long? SuggestionId { get; set; }
}

public sealed record PaperAccount(double Capital, double RiskPercent)
{
    public static readonly PaperAccount Default = new(10_000, 1);
}

public sealed record OpenPaperTradeRequest(string Symbol, TradeDirection Direction, double? Entry, double StopLoss, double TakeProfit, long? SuggestionId);

public sealed record PaperTradeDto(
    long Id,
    string Symbol,
    string Name,
    TradeDirection Direction,
    double Quantity,
    double EntryPrice,
    double StopLoss,
    double TakeProfit,
    DateTimeOffset OpenedAt,
    PaperStatus Status,
    DateTimeOffset? ClosedAt,
    double? ExitPrice,
    double? CurrentPrice,
    double Pnl,
    double RMultiple);

public sealed record PaperSummary(
    double Capital,
    double Equity,
    double RealizedPnl,
    double UnrealizedPnl,
    int OpenCount,
    int ClosedCount,
    double WinRate,
    double AverageR,
    double TotalR);

public sealed record PaperOverview(PaperAccount Account, PaperSummary Summary, IReadOnlyList<PaperTradeDto> Open, IReadOnlyList<PaperTradeDto> Closed);

public sealed record PaperOutcome(PaperStatus Status, double ExitPrice, DateTimeOffset ClosedAt);

public static class PaperCalculator
{
    public static double Size(double capital, double riskPercent, double entry, double stopLoss)
    {
        double distance = Math.Abs(entry - stopLoss);
        return distance <= 0 || capital <= 0 || riskPercent <= 0 ? 0 : Math.Round(capital * riskPercent / 100 / distance, 4);
    }

    public static double Pnl(PaperTrade trade, double price) =>
        (trade.Direction == TradeDirection.Buy ? price - trade.EntryPrice : trade.EntryPrice - price) * trade.Quantity;

    public static double RMultiple(PaperTrade trade, double price)
    {
        double risk = Math.Abs(trade.EntryPrice - trade.StopLoss);
        return risk <= 0 ? 0 : (trade.Direction == TradeDirection.Buy ? price - trade.EntryPrice : trade.EntryPrice - price) / risk;
    }

    public static PaperOutcome? Resolve(PaperTrade trade, IReadOnlyList<Candle> candles)
    {
        bool buy = trade.Direction == TradeDirection.Buy;

        foreach (Candle candle in candles.Where(c => c.Time > trade.OpenedAt).OrderBy(c => c.Time))
        {
            bool hitStop = buy ? candle.Low <= trade.StopLoss : candle.High >= trade.StopLoss;
            bool hitTarget = buy ? candle.High >= trade.TakeProfit : candle.Low <= trade.TakeProfit;

            if (hitStop)
            {
                return new PaperOutcome(PaperStatus.HitStopLoss, trade.StopLoss, candle.Time);
            }

            if (hitTarget)
            {
                return new PaperOutcome(PaperStatus.HitTakeProfit, trade.TakeProfit, candle.Time);
            }
        }

        return null;
    }

    public static IReadOnlyList<string> Validate(OpenPaperTradeRequest request, double entry)
    {
        List<string> errors = [];
        bool buy = request.Direction == TradeDirection.Buy;

        if (entry <= 0)
        {
            errors.Add("Sem preço de entrada.");
        }
        else if (buy && !(request.StopLoss < entry && request.TakeProfit > entry))
        {
            errors.Add("Numa compra, o stop loss tem de ficar abaixo da entrada e o take profit acima.");
        }
        else if (!buy && !(request.StopLoss > entry && request.TakeProfit < entry))
        {
            errors.Add("Numa venda, o stop loss tem de ficar acima da entrada e o take profit abaixo.");
        }

        return errors;
    }

    public static PaperSummary Summarize(PaperAccount account, IReadOnlyList<PaperTradeDto> open, IReadOnlyList<PaperTradeDto> closed)
    {
        double realized = closed.Sum(t => t.Pnl);
        double unrealized = open.Sum(t => t.Pnl);
        int wins = closed.Count(t => t.Pnl > 0);

        return new PaperSummary(
            account.Capital,
            account.Capital + realized + unrealized,
            realized,
            unrealized,
            open.Count,
            closed.Count,
            closed.Count == 0 ? 0 : (double)wins / closed.Count,
            closed.Count == 0 ? 0 : closed.Average(t => t.RMultiple),
            closed.Sum(t => t.RMultiple));
    }
}
