using Trader.Core.Models;

namespace Trader.Infrastructure.Suggestions;

internal static class MetaModelStore
{
    public static string SettingKey(SuggestionHorizon horizon) => $"meta-model:{horizon}";

    public static string CacheKey(SuggestionHorizon horizon) => $"meta-model-cache:{horizon}";
}
