using Trader.Core.Intraday;
using Trader.Core.Models;

namespace Trader.Core.Alerts;

public enum RuleDirection
{
    Any,
    Up,
    Down
}

public enum RuleSentiment
{
    Any,
    Positive,
    Negative
}

public sealed class AlertRule
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public AssetClass? AssetClass { get; set; }
    public string? Symbol { get; set; }
    public double? MinRelativeVolume { get; set; }
    public double? MinChangePercent { get; set; }
    public RuleDirection Direction { get; set; }
    public RadarEventKind? EventKind { get; set; }
    public RuleSentiment Sentiment { get; set; }
    public int CooldownMinutes { get; set; } = 60;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record AlertRuleDto(
    long Id,
    string Name,
    bool Enabled,
    AssetClass? AssetClass,
    string? Symbol,
    double? MinRelativeVolume,
    double? MinChangePercent,
    RuleDirection Direction,
    RadarEventKind? EventKind,
    RuleSentiment Sentiment,
    int CooldownMinutes);

public sealed record RuleMatch(bool Matched, string Reason);

public static class AlertRuleEvaluator
{
    public const double SentimentThreshold = 0.25;
    public const int MaxRules = 25;

    public static IReadOnlyList<string> Validate(AlertRuleDto rule)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 60)
        {
            errors.Add("Dá um nome à regra (até 60 caracteres).");
        }

        if (rule.MinRelativeVolume is null && rule.MinChangePercent is null && rule.EventKind is null && rule.Sentiment == RuleSentiment.Any)
        {
            errors.Add("Define pelo menos uma condição (volume, variação, evento ou sentimento).");
        }

        if (rule.MinRelativeVolume is < 0.5 or > 50)
        {
            errors.Add("O volume relativo tem de estar entre 0,5 e 50.");
        }

        if (rule.MinChangePercent is < 0.1 or > 100)
        {
            errors.Add("A variação mínima tem de estar entre 0,1% e 100%.");
        }

        if (rule.CooldownMinutes is < 5 or > 1440)
        {
            errors.Add("O intervalo entre alertas tem de estar entre 5 minutos e 24 horas.");
        }

        if (rule.Symbol is { Length: > 32 })
        {
            errors.Add("Símbolo inválido.");
        }

        return errors;
    }

    public static RuleMatch Evaluate(AlertRule rule, RadarRow row, IReadOnlyList<RadarEvent> symbolEvents, double? recentSentiment)
    {
        if (!rule.Enabled)
        {
            return new RuleMatch(false, string.Empty);
        }

        if (rule.Symbol is { Length: > 0 } symbol && !symbol.Equals(row.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return new RuleMatch(false, string.Empty);
        }

        if (rule.AssetClass is { } assetClass && row.AssetClass != assetClass)
        {
            return new RuleMatch(false, string.Empty);
        }

        List<string> reasons = [];
        double change = row.DayChangePercent ?? 0;

        if (rule.Direction == RuleDirection.Up && change <= 0 || rule.Direction == RuleDirection.Down && change >= 0)
        {
            return new RuleMatch(false, string.Empty);
        }

        if (rule.MinRelativeVolume is { } minVolume)
        {
            if (row.RelativeVolume is not { } volume || volume < minVolume)
            {
                return new RuleMatch(false, string.Empty);
            }

            reasons.Add($"volume {volume.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}× o normal");
        }

        if (rule.MinChangePercent is { } minChange)
        {
            if (Math.Abs(change) < minChange)
            {
                return new RuleMatch(false, string.Empty);
            }

            reasons.Add($"variação {change.ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture)}%");
        }

        if (rule.EventKind is { } kind)
        {
            RadarEvent? radarEvent = symbolEvents.FirstOrDefault(e => e.Kind == kind
                && (rule.Direction == RuleDirection.Any || (rule.Direction == RuleDirection.Up) == (e.Direction == TradeDirection.Buy)));

            if (radarEvent is null)
            {
                return new RuleMatch(false, string.Empty);
            }

            reasons.Add(radarEvent.Detail);
        }

        if (rule.Sentiment != RuleSentiment.Any)
        {
            bool positive = rule.Sentiment == RuleSentiment.Positive;

            if (recentSentiment is not { } sentiment || (positive ? sentiment < SentimentThreshold : sentiment > -SentimentThreshold))
            {
                return new RuleMatch(false, string.Empty);
            }

            reasons.Add($"notícias {(positive ? "positivas" : "negativas")} ({sentiment.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture)})");
        }

        return reasons.Count == 0 ? new RuleMatch(false, string.Empty) : new RuleMatch(true, string.Join(" · ", reasons));
    }

    public static bool InQuietHours(int hour, int? fromHour, int? toHour)
    {
        if (fromHour is not { } from || toHour is not { } to || from == to)
        {
            return false;
        }

        return from < to ? hour >= from && hour < to : hour >= from || hour < to;
    }

    public static AlertRuleDto ToDto(AlertRule rule) => new(
        rule.Id,
        rule.Name,
        rule.Enabled,
        rule.AssetClass,
        rule.Symbol,
        rule.MinRelativeVolume,
        rule.MinChangePercent,
        rule.Direction,
        rule.EventKind,
        rule.Sentiment,
        rule.CooldownMinutes);
}
