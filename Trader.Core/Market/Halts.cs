namespace Trader.Core.Market;

public sealed record TradingHalt(
    string Symbol,
    string Name,
    string Market,
    string ReasonCode,
    string Reason,
    DateTimeOffset HaltedAt,
    DateTimeOffset? ResumedAt)
{
    public bool IsActive => ResumedAt is null;
}

public static class HaltReasons
{
    public static string Describe(string code) => code.ToUpperInvariant() switch
    {
        "T1" => "Notícias pendentes",
        "T2" => "Notícias divulgadas",
        "T5" => "Pausa por volatilidade (um só ativo)",
        "T6" => "Atividade extraordinária no mercado",
        "T8" => "Suspensão de ETF",
        "T12" => "Informação adicional pedida pela bolsa",
        "H4" => "Incumprimento das regras de cotação",
        "H9" => "Atraso na entrega de relatórios",
        "H10" => "Suspensão pela SEC",
        "H11" => "Outros motivos regulatórios",
        "O1" => "Operações de mercado",
        "LUDP" => "Pausa por limite de preço (LULD)",
        "LUDS" => "Pausa por limite de preço (LULD, straddle)",
        "MWC1" or "MWC2" or "MWC3" or "MWC0" => "Travão de todo o mercado",
        "IPO1" => "IPO ainda sem negociação",
        "M1" => "Chamada corporativa",
        "M2" => "Cotação indisponível",
        _ => string.IsNullOrWhiteSpace(code) ? "Motivo não indicado" : $"Código {code}"
    };
}

public sealed record CompanyProfile(string Symbol, double ShareOutstandingMillions, double? MarketCapMillions, string Industry)
{
    public const double LowShareCountMillions = 30;

    public bool LowShareCount => ShareOutstandingMillions > 0 && ShareOutstandingMillions < LowShareCountMillions;
}
