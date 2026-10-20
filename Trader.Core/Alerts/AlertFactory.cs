using System.Globalization;
using Trader.Core.Models;

namespace Trader.Core.Alerts;

public static class AlertFactory
{
    public static Alert ForNewSuggestion(Suggestion suggestion, DateTimeOffset now) => new()
    {
        Kind = AlertKind.NewSuggestion,
        Symbol = suggestion.Symbol,
        Title = $"Nova sugestão: {(suggestion.Direction == TradeDirection.Buy ? "COMPRA" : "VENDA")} {suggestion.Symbol}",
        Message = string.Create(
            CultureInfo.InvariantCulture,
            $"{suggestion.Confidence * 100:0}% de certeza · entrada {suggestion.Entry:0.#####} · SL {suggestion.StopLoss:0.#####} · TP {suggestion.TakeProfit:0.#####}"),
        SuggestionId = suggestion.Id,
        CreatedAt = now
    };

    public static Alert? ForOutcome(Suggestion suggestion, DateTimeOffset now)
    {
        (AlertKind Kind, string Label)? outcome = suggestion.Status switch
        {
            SuggestionStatus.HitTakeProfit => (AlertKind.TakeProfit, "Take profit atingido"),
            SuggestionStatus.HitStopLoss => (AlertKind.StopLoss, "Stop loss atingido"),
            SuggestionStatus.Expired => (AlertKind.Expired, "Sugestão expirou"),
            _ => null
        };

        if (outcome is null)
        {
            return null;
        }

        return new Alert
        {
            Kind = outcome.Value.Kind,
            Symbol = suggestion.Symbol,
            Title = $"{outcome.Value.Label}: {suggestion.Symbol}",
            Message = string.Create(CultureInfo.InvariantCulture, $"Resultado {suggestion.RMultiple ?? 0:+0.00;-0.00;0.00} R · saída {suggestion.ExitPrice ?? 0:0.#####}"),
            SuggestionId = suggestion.Id,
            CreatedAt = now
        };
    }
}
