using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface IDisplayCurrency
{
    Task<string> GetAsync(CancellationToken cancellationToken);

    Task<string?> SetAsync(string currency, CancellationToken cancellationToken);

    Task<DisplayConversion> GetConversionAsync(CancellationToken cancellationToken);

    Task<DisplayConversion> GetConversionAsync(string currency, CancellationToken cancellationToken);
}

public static class DisplayCurrencyExtensions
{
    public static Task<DisplayConversion> GetConversionAsync(this IDisplayCurrency display, UserPreferences preferences, CancellationToken cancellationToken) =>
        preferences.DisplayCurrency is { } currency
            ? display.GetConversionAsync(currency, cancellationToken)
            : display.GetConversionAsync(cancellationToken);
}
