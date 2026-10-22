using System.Security.Claims;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Web.Endpoints;

internal static class UserDisplay
{
    public static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public static async Task<DisplayConversion> ConversionAsync(ClaimsPrincipal user, IUserPreferences preferences, IDisplayCurrency display, CancellationToken cancellationToken) =>
        await display.GetConversionAsync(await preferences.GetAsync(UserId(user), cancellationToken), cancellationToken);

    public static async Task<HashSet<string>?> AllowedSymbolsAsync(UserPreferences preferences, IInstrumentService instruments, CancellationToken cancellationToken)
    {
        if (preferences.EnabledClasses.Count == 0)
        {
            return null;
        }

        return (await instruments.ListAsync(cancellationToken))
            .Where(i => preferences.Allows(i.AssetClass))
            .Select(i => i.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
