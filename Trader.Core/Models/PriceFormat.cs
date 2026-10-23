namespace Trader.Core.Models;

public static class PriceFormat
{
    public static string Adaptive(double value, System.Globalization.CultureInfo culture)
    {
        double magnitude = Math.Abs(value);

        if (magnitude == 0)
        {
            return "0";
        }

        if (magnitude >= 100)
        {
            return value.ToString("N2", culture);
        }

        if (magnitude >= 1)
        {
            return value.ToString("N3", culture);
        }

        if (magnitude >= 0.01)
        {
            return value.ToString("N5", culture);
        }

        int decimals = Math.Min(12, (int)Math.Ceiling(-Math.Log10(magnitude)) + 3);
        return value.ToString("N" + decimals, culture);
    }

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
