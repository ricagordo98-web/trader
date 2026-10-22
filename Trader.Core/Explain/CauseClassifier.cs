using System.Text.RegularExpressions;

namespace Trader.Core.Explain;

public static class CauseClassifier
{
    private static readonly (CauseCategory Category, Regex Pattern)[] Rules =
    [
        (CauseCategory.Earnings, Build(@"earnings|\beps\b|revenue|quarterly results|first[- ]quarter|second[- ]quarter|third[- ]quarter|fourth[- ]quarter|\bq[1-4]\b|net income|profit|resultados|lucros|receitas")),
        (CauseCategory.Macro, Build(@"\bfed\b|federal reserve|central bank|\becb\b|\bfomc\b|inflation|\bcpi\b|rate cuts?|rate hikes?|interest rates?|tariffs?|jobs report|payrolls|\bopec\b|\bwar\b|conflict|ceasefire|geopolit|treasury yields?|sanctions|infla[çc]|taxas de juro|tarifas|guerra")),
        (CauseCategory.Guidance, Build(@"guidance|(?:raises?|cuts?|lowers?|slashes|trims?|withdraws?|reaffirms?|narrows?) (?:its |their )?(?:full[- ]year |annual |quarterly |q[1-4] )?(?:outlook|forecast|projections?)|previs|perspetivas")),
        (CauseCategory.Analyst, Build(@"upgrad|downgrad|price target|analyst|initiates coverage|reiterat|overweight|underweight|outperform|underperform|rating|recomenda")),
        (CauseCategory.MergerAcquisition, Build(@"acqui|merger|takeover|buyout|to buy |to be bought|bid for|tender offer|spin[- ]?off|aquisi|fus[ãa]o|oferta de compra")),
        (CauseCategory.Management, Build(@"\bceo\b|\bcfo\b|chairman|chief executive|steps? down|resign|appoint|names new|executive|demiss|nomea|presidente executivo")),
        (CauseCategory.Legal, Build(@"lawsuit|\bsues?\b|sued|investigation|probe|subpoena|settle|antitrust|fine[sd]?\b|regulator|\bfda\b|recall|\bban\b|banned|sanction|tribunal|investiga|multa|processo")),
        (CauseCategory.Product, Build(@"launch|unveil|new product|partnership|contract|approval|approves|wins? |order|lan[çc]a|parceria|contrato|aprova")),
        (CauseCategory.Dividend, Build(@"dividend|buyback|repurchase|stock split|dividendo|recompra")),
        (CauseCategory.Insider, Build(@"insider|form 4|director buys|director sells"))
    ];

    public static CauseCategory Categorize(string text)
    {
        foreach ((CauseCategory category, Regex pattern) in Rules)
        {
            if (pattern.IsMatch(text))
            {
                return category;
            }
        }

        return CauseCategory.Other;
    }

    public static string Label(CauseCategory category) => category switch
    {
        CauseCategory.Earnings => "Resultados",
        CauseCategory.Guidance => "Guidance",
        CauseCategory.Analyst => "Analistas",
        CauseCategory.MergerAcquisition => "M&A",
        CauseCategory.Management => "Gestão",
        CauseCategory.Legal => "Legal/regulatório",
        CauseCategory.Product => "Produto/contratos",
        CauseCategory.Dividend => "Dividendos/recompras",
        CauseCategory.Macro => "Macro",
        CauseCategory.Insider => "Insiders",
        CauseCategory.Filing => "Filing SEC",
        _ => "Outra"
    };

    private static Regex Build(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
