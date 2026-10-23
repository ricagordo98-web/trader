using Trader.Core.Models;

namespace Trader.Core.Alerts;

public static class NtfyRouting
{
    public static string ResolveTopic(NtfySettings global, UserPreferences? preferences) =>
        preferences?.NtfyTopic is { Length: > 0 } own ? own : global.Topic;

    public static bool ShouldPush(AlertDto alert, bool quietFlag, UserPreferences? preferences, int hour)
    {
        if (quietFlag)
        {
            return false;
        }

        if (preferences is null || IsCritical(alert.Kind))
        {
            return true;
        }

        return !AlertRuleEvaluator.InQuietHours(hour, preferences.QuietFromHour, preferences.QuietToHour);
    }

    public static bool IsCritical(AlertKind kind) => kind is AlertKind.StopLoss or AlertKind.TradeStopLoss;

    public static string? ClickUrl(NtfySettings settings, AlertDto alert) =>
        settings.PublicUrl.Length == 0 ? null : settings.PublicUrl.TrimEnd('/') + AlertLinks.For(alert);
}
