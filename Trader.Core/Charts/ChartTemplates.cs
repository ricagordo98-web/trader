namespace Trader.Core.Charts;

public sealed record ChartTemplate(string Name, IReadOnlyList<string> Indicators);

public static class ChartTemplateValidator
{
    public const int MaxTemplates = 20;

    private static readonly HashSet<string> KnownIndicators =
        ["MA", "EMA", "BOLL", "SAR", "VOL", "MACD", "RSI", "KDJ", "OBV", "CCI"];

    public static IReadOnlyList<string> Validate(IReadOnlyList<ChartTemplate> templates)
    {
        List<string> errors = [];

        if (templates.Count > MaxTemplates)
        {
            errors.Add($"No máximo {MaxTemplates} modelos.");
        }

        if (templates.Any(t => string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 40))
        {
            errors.Add("O nome do modelo deve ter entre 1 e 40 caracteres.");
        }

        if (templates.Select(t => t.Name.Trim().ToLowerInvariant()).Distinct().Count() != templates.Count)
        {
            errors.Add("Os nomes dos modelos têm de ser diferentes.");
        }

        if (templates.Any(t => t.Indicators.Count > 12 || t.Indicators.Any(i => !KnownIndicators.Contains(i))))
        {
            errors.Add("Indicador desconhecido no modelo.");
        }

        return errors;
    }
}
