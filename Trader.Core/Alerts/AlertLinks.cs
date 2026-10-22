namespace Trader.Core.Alerts;

public static class AlertLinks
{
    private const string GroupedSuggestions = "SUGESTÕES";
    private const string GroupedRadar = "RADAR";

    public static string For(AlertDto alert) => alert.Kind switch
    {
        AlertKind.TradeStopLoss or AlertKind.TradeTakeProfit => alert.TradeId > 0 ? $"/journal?trade={alert.TradeId}" : "/journal",
        AlertKind.NewSuggestion or AlertKind.TakeProfit or AlertKind.StopLoss or AlertKind.Expired =>
            alert.SuggestionId > 0 ? $"/prediction?suggestion={alert.SuggestionId}" : "/prediction",
        AlertKind.PriceTarget => ChartLink(alert.Symbol),
        AlertKind.Radar => alert.Symbol == GroupedRadar ? "/prediction?tab=radar" : ChartLink(alert.Symbol),
        AlertKind.Calendar => "/calendar",
        _ => "/"
    };

    private static string ChartLink(string symbol) =>
        symbol.Length == 0 || symbol == GroupedSuggestions || symbol == GroupedRadar ? "/trading" : $"/trading?symbol={Uri.EscapeDataString(symbol)}";
}
