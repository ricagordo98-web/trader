using System.Globalization;
using System.Text;

namespace Trader.Core.Explain;

public static class ExplanationText
{
    private const double StrongCauseScore = 0.6;
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static string Percent(double value) => value.ToString("+0.0#;-0.0#;0.0", Pt) + "%";

    public static string Summary(MoveAnalysis analysis, IReadOnlyList<MoveCause> causes)
    {
        StringBuilder text = new();
        string verb = analysis.ChangePercent >= 0 ? "subiu" : "desceu";

        text.Append($"{analysis.Symbol} {verb} {Math.Abs(analysis.ChangePercent).ToString("0.0#", Pt)}% no período analisado");

        if (analysis.TypicalMovePercent > 0)
        {
            text.Append($" (o movimento típico seria ±{analysis.TypicalMovePercent.ToString("0.0#", Pt)}%)");
        }

        text.Append('.');

        if (analysis.Scope == MoveScope.Small)
        {
            text.Append(" É um movimento pequeno, dentro do normal para este ativo; não há nada de especial a explicar.");
            return text.ToString();
        }

        text.Append(' ').Append(ScopeSentence(analysis));

        if (analysis.VolumeRatio is { } volume && volume >= 1.5)
        {
            text.Append($" O volume esteve {volume.ToString("0.0", Pt)}× acima do normal.");
        }

        if (analysis.GapPercent is { } gap)
        {
            text.Append($" Houve um gap de {Percent(gap)} na abertura.");
        }

        text.Append(' ').Append(CauseSentence(analysis, causes));
        return text.ToString();
    }

    public static string WithoutCitations(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s?\[E\d+\]", string.Empty).Trim();

    public static string? OneLiner(MoveExplanation explanation)
    {
        MoveAnalysis analysis = explanation.Analysis;

        if (analysis.Scope == MoveScope.Small)
        {
            return null;
        }

        if (explanation.Causes.Count > 0 && explanation.Causes[0].Score >= StrongCauseScore && analysis.Scope != MoveScope.MarketWide)
        {
            MoveCause top = explanation.Causes[0];
            string title = top.Title.Length > 110 ? top.Title[..109] + "…" : top.Title;
            return $"Provável causa: {CauseClassifier.Label(top.Category).ToLowerInvariant()} — {title}";
        }

        return analysis.Scope switch
        {
            MoveScope.MarketWide when analysis.Benchmark is { } b => $"Acompanha o mercado ({b.Name} {Percent(b.ChangePercent)})",
            MoveScope.Sector => $"Movimento de setor ({PeerList(analysis)})",
            _ => null
        };
    }

    public static string ScopeSentence(MoveAnalysis analysis) => analysis.Scope switch
    {
        MoveScope.MarketWide when analysis.Benchmark is { } b =>
            $"Acompanha o mercado: {b.Name} variou {Percent(b.ChangePercent)}, o que explica cerca de {Percent(b.ExpectedPercent)} deste ativo.",
        MoveScope.Sector =>
            $"Movimento partilhado com ativos correlacionados ({PeerList(analysis)}), o que aponta para uma causa de setor.",
        MoveScope.CompanySpecific when analysis.Benchmark is { } b =>
            $"O mercado ({b.Name} {Percent(b.ChangePercent)}) explica pouco: o movimento parece específico deste ativo (resíduo {Percent(analysis.ResidualPercent)}).",
        MoveScope.CompanySpecific =>
            "O movimento parece específico deste ativo, sem um índice de referência que o explique.",
        _ when analysis.Benchmark is { } b =>
            $"O mercado ({b.Name} {Percent(b.ChangePercent)}) explica só parte do movimento (resíduo {Percent(analysis.ResidualPercent)}).",
        _ => "O movimento só é parcialmente explicado pelo contexto de mercado."
    };

    public static string CauseSentence(MoveAnalysis analysis, IReadOnlyList<MoveCause> causes)
    {
        if (causes.Count == 0)
        {
            return analysis.Scope == MoveScope.MarketWide
                ? "Não encontrei notícias específicas deste ativo, o que é coerente com um movimento de mercado."
                : "Não encontrei notícias nem eventos que expliquem o movimento nas fontes consultadas.";
        }

        MoveCause top = causes[0];
        string when = top.LeadMinutes >= 90
            ? $"cerca de {(top.LeadMinutes / 60).ToString(Pt)} h antes do maior movimento"
            : top.LeadMinutes >= 0 ? "pouco antes do maior movimento" : "logo a seguir ao maior movimento";

        return $"Causa mais provável: {CauseClassifier.Label(top.Category).ToLowerInvariant()} — «{top.Title}» ({top.Source}, {when}). Confiança {top.Confidence.ToLowerInvariant()}; é uma atribuição provável, não provada.";
    }

    private static string PeerList(MoveAnalysis analysis) =>
        string.Join(", ", analysis.Peers.Take(4).Select(p => $"{p.Symbol} {Percent(p.ChangePercent)}"));
}
