using System.Globalization;
using System.Text;

namespace Trader.Core.Explain;

public static class NarrationPrompt
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static string Facts(MoveAnalysis analysis, IReadOnlyList<MoveCause> causes)
    {
        StringBuilder facts = new();
        string verb = analysis.ChangePercent >= 0 ? "subiu" : "desceu";

        facts.AppendLine($"Ativo: {analysis.Symbol} ({analysis.Name})");
        facts.AppendLine($"Movimento: {verb} {Math.Abs(analysis.ChangePercent).ToString("0.0#", Pt)}% entre {analysis.From:yyyy-MM-dd HH:mm} e {analysis.To:yyyy-MM-dd HH:mm} UTC");
        facts.AppendLine($"Movimento típico neste período: {analysis.TypicalMovePercent.ToString("0.0#", Pt)}% (z = {analysis.ZScore.ToString("0.0", Pt)})");
        facts.AppendLine($"O maior movimento numa só vela foi às {analysis.PeakTime:HH:mm} UTC, de {ExplanationText.Percent(analysis.PeakChangePercent)}");

        if (analysis.VolumeRatio is { } volume)
        {
            facts.AppendLine($"Volume: {volume.ToString("0.0", Pt)}× o normal");
        }

        if (analysis.GapPercent is { } gap)
        {
            facts.AppendLine($"Gap na abertura: {ExplanationText.Percent(gap)}");
        }

        if (analysis.Benchmark is { } benchmark)
        {
            facts.AppendLine($"Mercado de referência ({benchmark.Name}): {ExplanationText.Percent(benchmark.ChangePercent)}; esperado para este ativo: {ExplanationText.Percent(benchmark.ExpectedPercent)}; resíduo: {ExplanationText.Percent(analysis.ResidualPercent)}");
        }

        if (analysis.Peers.Count > 0)
        {
            facts.AppendLine("Ativos correlacionados: " + string.Join(", ", analysis.Peers.Take(5).Select(p => $"{p.Symbol} {ExplanationText.Percent(p.ChangePercent)}")));
        }

        facts.AppendLine($"Classificação do movimento: {ScopeLabel(analysis.Scope)}");
        facts.AppendLine(causes.Count == 0 ? "Evidências: nenhuma encontrada." : "Evidências:");

        foreach (MoveCause cause in causes)
        {
            string timing = cause.LeadMinutes >= 0
                ? $"{cause.Time:yyyy-MM-dd HH:mm} UTC, {Duration(cause.LeadMinutes)} antes do maior movimento"
                : $"{cause.Time:yyyy-MM-dd HH:mm} UTC, {Duration(-cause.LeadMinutes)} depois do maior movimento";

            facts.AppendLine($"[{cause.Id}] ({CauseClassifier.Label(cause.Category)}; {cause.Source}; {timing}{Tone(cause.Sentiment)}) {cause.Title}{(cause.Detail.Length > 0 ? " — " + cause.Detail : string.Empty)}");
        }

        return facts.ToString().TrimEnd();
    }

    public static string Build(string facts) =>
        "És um analista que explica a um investidor particular, em português de Portugal, PORQUE um ativo subiu ou desceu. "
        + "Usa APENAS os factos abaixo.\n"
        + "Regras:\n"
        + "1. Não inventes números, datas, nomes, empresas, citações nem causas que não estejam nos factos.\n"
        + "2. Só podes atribuir o movimento a uma evidência listada, citando-a assim: [E1].\n"
        + "3. Se nenhuma evidência explicar o movimento, diz com clareza que não há causa identificada nos dados e não adivinhes nem especules sobre eventos que não estejam nos factos.\n"
        + "4. Se a classificação for \"acompanha o mercado\" ou \"movimento de setor\", começa por dizer isso; só podes mencionar uma evidência como contributo secundário.\n"
        + "5. Se o sentimento de uma evidência for contrário ao movimento (por exemplo notícia positiva e preço a descer), essa evidência NÃO explica o movimento.\n"
        + "6. Usa linguagem de probabilidade (provável, aponta para); nunca certeza. Não dês conselhos de investimento.\n"
        + "7. Descreve cada evidência com fidelidade ao título: não transformes previsões, expectativas ou opiniões em factos consumados (por exemplo, \"espera-se que divulgue\" não é \"divulgou\").\n"
        + "8. No máximo 4 frases, em bom português de Portugal.\n"
        + "Responde apenas com JSON: {\"summary\": \"texto\", \"confidence\": \"alta|media|baixa|nenhuma\", \"used\": [\"E1\"]}\n\n"
        + "FACTOS:\n" + facts;

    private static string ScopeLabel(MoveScope scope) => scope switch
    {
        MoveScope.Small => "movimento pequeno",
        MoveScope.MarketWide => "acompanha o mercado",
        MoveScope.Sector => "movimento de setor (ativos correlacionados mexeram-se igual)",
        MoveScope.CompanySpecific => "específico do ativo",
        _ => "misto"
    };

    private static string Tone(double? sentiment) => sentiment switch
    {
        >= 0.3 => "; sentimento da notícia: positivo",
        <= -0.3 => "; sentimento da notícia: negativo",
        null => string.Empty,
        _ => "; sentimento da notícia: neutro"
    };

    private static string Duration(int minutes) =>
        minutes >= 120 ? $"{minutes / 60} h" : $"{minutes} min";
}
