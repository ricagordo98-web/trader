using Trader.Core.Journal;
using Trader.Core.Models;

namespace Trader.Core.Alerts;

public enum PriceCondition
{
    Above,
    Below
}

public enum TradeLevel
{
    StopLoss,
    TakeProfit
}

public sealed class PriceAlert
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public PriceCondition Condition { get; set; }

    public double Price { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? TriggeredAt { get; set; }
}

public sealed record PriceAlertDto(long Id, string Symbol, PriceCondition Condition, double Price, string Note, DateTimeOffset CreatedAt, DateTimeOffset? TriggeredAt);

public sealed record CreatePriceAlertRequest(string Symbol, PriceCondition Condition, double Price, string? Note);

public static class LevelChecker
{
    public static TradeLevel? Check(JournalTrade trade, double price)
    {
        if (trade.Status != TradeStatus.Open)
        {
            return null;
        }

        bool buy = trade.Direction == TradeDirection.Buy;

        if (trade is { StopLoss: { } stop, StopLossAlerted: false } && (buy ? price <= stop : price >= stop))
        {
            return TradeLevel.StopLoss;
        }

        if (trade is { TakeProfit: { } target, TakeProfitAlerted: false } && (buy ? price >= target : price <= target))
        {
            return TradeLevel.TakeProfit;
        }

        return null;
    }

    public static bool IsTriggered(PriceAlert alert, double price) =>
        alert.TriggeredAt is null && (alert.Condition == PriceCondition.Above ? price >= alert.Price : price <= alert.Price);
}

public static class PriceAlertValidator
{
    public static IReadOnlyList<string> Validate(CreatePriceAlertRequest request)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol.Length > 32)
        {
            errors.Add("Indica o ativo.");
        }

        if (!double.IsFinite(request.Price) || request.Price <= 0)
        {
            errors.Add("O preço deve ser maior que zero.");
        }

        if (request.Note is { Length: > 200 })
        {
            errors.Add("A nota pode ter no máximo 200 caracteres.");
        }

        return errors;
    }
}
