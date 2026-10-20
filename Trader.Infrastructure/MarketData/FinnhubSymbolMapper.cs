using System.Text.RegularExpressions;

namespace Trader.Infrastructure.MarketData;

public static partial class FinnhubSymbolMapper
{
    public static string? ToFinnhub(string symbol)
    {
        string upper = symbol.Trim().ToUpperInvariant();

        if (Forex().Match(upper) is { Success: true } forex)
        {
            return $"OANDA:{forex.Groups[1].Value}_{forex.Groups[2].Value}";
        }

        if (Crypto().Match(upper) is { Success: true } crypto)
        {
            return $"BINANCE:{crypto.Groups[1].Value}USDT";
        }

        return UsListing().IsMatch(upper) ? upper : null;
    }

    [GeneratedRegex("^([A-Z]{3})([A-Z]{3})=X$")]
    private static partial Regex Forex();

    [GeneratedRegex("^([A-Z0-9]{2,6})-USD$")]
    private static partial Regex Crypto();

    [GeneratedRegex("^[A-Z]{1,5}$")]
    private static partial Regex UsListing();
}
