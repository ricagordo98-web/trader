using Trader.Core.Backtesting;
using Trader.Core.Models;

namespace Trader.Core.Scoring;

public static class SuggestionCosts
{
    public static double CostR(SuggestionDto suggestion, AssetClass assetClass, CostModel costs)
    {
        double risk = Math.Abs(suggestion.Entry - suggestion.StopLoss);

        if (risk <= 0 || suggestion.Entry <= 0)
        {
            return 0;
        }

        double days = suggestion.Horizon == SuggestionHorizon.Swing && suggestion.ClosedAt is { } closed
            ? Math.Max(0, (closed - suggestion.CreatedAt).TotalDays)
            : 0;

        return costs.CostInR(assetClass, suggestion.Entry, risk, days);
    }
}
