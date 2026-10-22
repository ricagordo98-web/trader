namespace Trader.Core.Models;

public sealed record UserPreferences(
    string Broker,
    string? DisplayCurrency,
    IReadOnlyList<AssetClass> EnabledClasses,
    bool NarrativeEnabled)
{
    public static readonly UserPreferences Default = new(BrokerCatalog.Other, null, [], true);

    public bool Allows(AssetClass assetClass) => EnabledClasses.Count == 0 || EnabledClasses.Contains(assetClass);
}

public sealed record BrokerOption(string Key, string Label);

public static class BrokerCatalog
{
    public const string Other = "other";

    public static readonly IReadOnlyList<BrokerOption> Options =
    [
        new("trading212-cfd", "Trading 212 (CFD)"),
        new("trading212-invest", "Trading 212 (Invest/ISA)"),
        new("xtb", "XTB"),
        new(Other, "Outra / nenhuma")
    ];

    public static string Label(string key) => Options.FirstOrDefault(o => o.Key == key)?.Label ?? Options[^1].Label;

    public static string? LabelOrNull(string key) => key == Other ? null : Options.FirstOrDefault(o => o.Key == key)?.Label;
}

public static class UserPreferencesValidator
{
    private static readonly string[] Currencies = ["EUR", "USD", "GBP"];

    public static IReadOnlyList<string> Validate(UserPreferences preferences)
    {
        List<string> errors = [];

        if (BrokerCatalog.Options.All(o => o.Key != preferences.Broker))
        {
            errors.Add("Corretora desconhecida.");
        }

        if (preferences.DisplayCurrency is { Length: > 0 } currency && !Currencies.Contains(currency))
        {
            errors.Add($"Moeda não suportada. Usa uma destas: {string.Join(", ", Currencies)}.");
        }

        if (preferences.EnabledClasses.Any(c => !Enum.IsDefined(c)))
        {
            errors.Add("Classe de ativos inválida.");
        }

        return errors;
    }
}
