using Trader.Core.Models;

namespace Trader.Shared.Components;

public sealed record TradePrefill(
    string Symbol,
    TradeDirection Direction,
    double Entry,
    double? StopLoss,
    double? TakeProfit,
    double Quantity,
    double FxRate,
    long? SuggestionId);
