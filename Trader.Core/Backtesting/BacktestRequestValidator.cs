namespace Trader.Core.Backtesting;

public static class BacktestRequestValidator
{
    private static readonly string[] AllowedRanges = ["2y", "5y", "10y"];

    public static IReadOnlyList<string> Validate(BacktestRequest request)
    {
        List<string> errors = [];

        if (!AllowedRanges.Contains(request.Range))
        {
            errors.Add("O período tem de ser 2y, 5y ou 10y.");
        }

        CostModel costs = request.Costs;

        foreach (double value in new[]
        {
            costs.StockRoundTripPercent, costs.EtfRoundTripPercent, costs.CommodityRoundTripPercent,
            costs.ForexRoundTripPercent, costs.CryptoRoundTripPercent, costs.IndexRoundTripPercent, costs.OvernightPercentPerDay
        })
        {
            if (value is < 0 or > 5 || double.IsNaN(value))
            {
                errors.Add("Os custos têm de estar entre 0% e 5%.");
                break;
            }
        }

        return errors;
    }
}
