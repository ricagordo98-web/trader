namespace Trader.Core.Models;

public static class PriceFormat
{
    public static int Decimals(AssetClass assetClass, string symbol, double? price)
    {
        if (assetClass == AssetClass.Forex)
        {
            return symbol.Contains("JPY", StringComparison.Ordinal) ? 3 : 5;
        }

        if (price is not { } value || value <= 0)
        {
            return 2;
        }

        if (assetClass == AssetClass.Crypto)
        {
            return value switch
            {
                < 0.0001 => 9,
                < 0.01 => 7,
                < 1 => 5,
                < 10 => 4,
                _ => 2
            };
        }

        return value < 1 ? 4 : 2;
    }
}
