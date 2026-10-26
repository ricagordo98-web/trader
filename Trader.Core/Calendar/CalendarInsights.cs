using System.Globalization;
using Trader.Core.Journal;
using Trader.Core.Models;

namespace Trader.Core.Calendar;

public enum EventImportance
{
    Low,
    Medium,
    High
}

public sealed record MacroInfo(EventImportance Importance, string What, string Why, string Reaction);

public static class MacroKnowledge
{
    private static readonly (string Key, MacroInfo Info)[] Entries =
    [
        ("FOMC", new MacroInfo(
            EventImportance.High,
            "Reunião em que a Reserva Federal (Fed) decide a taxa de juro dos EUA, seguida de uma conferência de imprensa.",
            "Os juros mexem com o custo do dinheiro e com o valor de todos os ativos. O tom da conferência pesa tanto como a decisão.",
            "Juros mais altos ou tom duro tendem a pressionar ações (sobretudo tecnologia), ouro e cripto e a reforçar o dólar. Cortes ou tom brando tendem a fazer o contrário. A volatilidade sobe nas horas à volta do anúncio.")),
        ("CPI", new MacroInfo(
            EventImportance.High,
            "Inflação ao consumidor dos EUA: quanto subiram os preços no último mês.",
            "É o dado que mais pesa nas decisões de juros da Fed.",
            "Acima do esperado: juros altos por mais tempo, pressão sobre ações de crescimento e ouro, dólar mais forte. Abaixo do esperado: o contrário.")),
        ("Non-Farm", new MacroInfo(
            EventImportance.High,
            "Número de empregos criados nos EUA no mês anterior (fora da agricultura) e taxa de desemprego.",
            "Mostra a força da economia e influencia as expectativas de juros.",
            "Emprego muito forte pode subir os juros esperados; muito fraco aumenta o receio de recessão. Os saltos costumam ser grandes em índices, dólar e ouro.")),
        ("PCE", new MacroInfo(
            EventImportance.High,
            "Inflação medida pelo consumo pessoal nos EUA, a medida de inflação preferida da Fed.",
            "Confirma ou contraria o que o CPI já sugeriu.",
            "Costuma mexer menos que o CPI, porque é parcialmente conhecido antes, mas surpresas pesam nas expectativas de juros.")),
        ("PIB", new MacroInfo(
            EventImportance.Medium,
            "Produto Interno Bruto dos EUA: o crescimento da economia no trimestre.",
            "Dá a imagem geral da economia e do risco de recessão.",
            "Crescimento forte ajuda ações cíclicas mas pode manter os juros altos; fraco aumenta o receio de recessão.")),
        ("PPI", new MacroInfo(
            EventImportance.Medium,
            "Preços na produção dos EUA: o que as empresas pagam antes de chegar ao consumidor.",
            "Antecipa a pressão sobre os preços no consumo e as margens das empresas.",
            "Surpresas para cima reforçam a ideia de inflação persistente; o efeito costuma ser menor que o do CPI.")),
        ("retalho", new MacroInfo(
            EventImportance.Medium,
            "Vendas a retalho dos EUA: quanto os consumidores gastaram no mês.",
            "O consumo é cerca de dois terços da economia americana.",
            "Vendas fortes ajudam consumo e retalho; fracas aumentam o receio de abrandamento."))
    ];

    public static MacroInfo? Find(string title) =>
        Entries.FirstOrDefault(e => title.Contains(e.Key, StringComparison.OrdinalIgnoreCase)).Info;
}

public sealed record Exposure(bool HasTrade, TradeDirection? TradeDirection, bool HasSuggestion, bool IsFavorite)
{
    public static readonly Exposure None = new(false, null, false, false);

    public bool Any => HasTrade || HasSuggestion || IsFavorite;

    public static Exposure For(string symbol, IReadOnlyList<JournalTradeDto> openTrades, IReadOnlyList<SuggestionDto> openSuggestions, IReadOnlyList<string> favorites)
    {
        JournalTradeDto? trade = openTrades.FirstOrDefault(t => t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        return new Exposure(
            trade is not null,
            trade?.Direction,
            openSuggestions.Any(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)),
            favorites.Contains(symbol, StringComparer.OrdinalIgnoreCase));
    }
}

public static class CalendarInsights
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static EventImportance ImportanceOf(CalendarEvent calendarEvent, Exposure exposure)
    {
        if (calendarEvent.Kind == CalendarKind.Macro)
        {
            return MacroKnowledge.Find(calendarEvent.Title)?.Importance ?? EventImportance.Low;
        }

        return exposure.HasTrade || exposure.HasSuggestion ? EventImportance.High : exposure.IsFavorite ? EventImportance.Medium : EventImportance.Low;
    }

    public static double? TypicalDailyMovePercent(IReadOnlyList<double> closes)
    {
        List<double> changes = [];

        for (int i = 1; i < closes.Count; i++)
        {
            if (closes[i - 1] > 0)
            {
                changes.Add((closes[i] - closes[i - 1]) / closes[i - 1] * 100);
            }
        }

        if (changes.Count < 5)
        {
            return null;
        }

        double mean = changes.Average();
        return Math.Sqrt(changes.Sum(c => (c - mean) * (c - mean)) / (changes.Count - 1));
    }

    public static string SessionLabel(CalendarEvent calendarEvent) => calendarEvent.Detail switch
    {
        var d when d.Contains("antes da abertura", StringComparison.OrdinalIgnoreCase) => "Antes da abertura",
        var d when d.Contains("depois do fecho", StringComparison.OrdinalIgnoreCase) => "Depois do fecho",
        var d when d.Contains("durante a sessão", StringComparison.OrdinalIgnoreCase) => "Durante a sessão",
        _ => "Hora por confirmar"
    };

    public static string BigMoney(double value) => value switch
    {
        >= 1e9 => $"{(value / 1e9).ToString("0.0#", Pt)} mil M",
        >= 1e6 => $"{(value / 1e6).ToString("0", Pt)} M",
        _ => value.ToString("0", Pt)
    };
}
