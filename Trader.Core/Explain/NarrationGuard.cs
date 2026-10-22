using System.Globalization;
using System.Text.RegularExpressions;

namespace Trader.Core.Explain;

public sealed record NarrationDraft(string Summary, string Confidence, IReadOnlyList<string> Used);

public static partial class NarrationGuard
{
    private const int MaxLength = 800;
    private const int FreeIntegerLimit = 10;
    private const double RoundingTolerance = 0.051;

    private static readonly HashSet<string> AllowedAcronyms = new(StringComparer.Ordinal)
    {
        "EPS", "CEO", "CFO", "EUR", "USD", "GBP", "SEC", "IA", "UTC", "ETF", "PIB", "FDA", "M&A", "FED", "OK", "UE", "EUA", "BCE", "UK", "EU", "US", "USA", "AI"
    };

    private static readonly string[] AdviceWords = ["recomendo", "aconselho", "deves comprar", "deves vender", "compra já", "vende já", "devias comprar", "devias vender", "should buy", "should sell"];

    public static IReadOnlyList<string> Validate(NarrationDraft draft, MoveAnalysis analysis, IReadOnlyList<MoveCause> causes)
    {
        List<string> violations = Validate(draft, NarrationPrompt.Facts(analysis, causes), causes.Select(c => c.Id).ToList()).ToList();
        violations.AddRange(CheckConsistency(draft, analysis, causes));
        return violations.Distinct().ToList();
    }

    private static IEnumerable<string> CheckConsistency(NarrationDraft draft, MoveAnalysis analysis, IReadOnlyList<MoveCause> causes)
    {
        string text = draft.Summary.ToLowerInvariant();
        List<string> cited = CitedIds(draft.Summary).Concat(draft.Used).Distinct().ToList();
        int direction = Math.Sign(analysis.ChangePercent);

        foreach (MoveCause cause in causes.Where(c => cited.Contains(c.Id) && c.Sentiment is { } s && Math.Abs(s) >= 0.3 && Math.Sign(s) == -direction))
        {
            yield return $"usa como causa uma notícia de sentido contrário ao movimento ({cause.Id})";
        }

        if (analysis.Scope == MoveScope.MarketWide && !(text.Contains("mercado") || text.Contains("índice") || (analysis.Benchmark is { } b && text.Contains(b.Name.ToLowerInvariant()))))
        {
            yield return "não diz que o movimento acompanha o mercado";
        }

        if (analysis.Scope == MoveScope.Sector && !(text.Contains("setor") || text.Contains("sector") || text.Contains("correlacion") || text.Contains("pares") || text.Contains("conjunto")))
        {
            yield return "não diz que é um movimento de setor";
        }

        if (causes.Count == 0 && analysis.Scope != MoveScope.MarketWide && !(text.Contains("não") || text.Contains("nenhum") || text.Contains("sem ")))
        {
            yield return "devia admitir que não há causa identificada";
        }
    }

    public static IReadOnlyList<string> Validate(NarrationDraft draft, string facts, IReadOnlyCollection<string> validIds)
    {
        List<string> violations = [];
        string text = draft.Summary;

        if (string.IsNullOrWhiteSpace(text))
        {
            return ["resposta vazia"];
        }

        if (text.Length > MaxLength)
        {
            violations.Add("texto demasiado longo");
        }

        foreach (string id in draft.Used.Concat(CitedIds(text)).Distinct())
        {
            if (!validIds.Contains(id))
            {
                violations.Add($"cita uma evidência inexistente ({id})");
            }
        }

        string withoutCitations = Citation().Replace(text, " ");
        CheckNumbers(withoutCitations, facts, violations);
        CheckTokens(withoutCitations, facts, violations);

        if (Url().IsMatch(text) && !Url().Matches(text).All(m => facts.Contains(m.Value, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add("contém um endereço que não está nos factos");
        }

        string lower = text.ToLowerInvariant();

        foreach (string advice in AdviceWords.Where(lower.Contains))
        {
            violations.Add($"dá aconselhamento ({advice})");
        }

        if (validIds.Count > 0 && draft.Confidence != "nenhuma" && draft.Used.Count == 0 && CitedIds(text).Count == 0)
        {
            violations.Add("atribui uma causa sem citar nenhuma evidência");
        }

        return violations.Distinct().ToList();
    }

    public static IReadOnlyList<string> CitedIds(string text) =>
        Citation().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    private static void CheckNumbers(string text, string facts, List<string> violations)
    {
        List<double> allowed = Numbers(facts).Select(n => n.Value).ToList();

        foreach ((double value, bool percent, string raw) in Numbers(text))
        {
            bool free = !percent && value == Math.Floor(value) && value <= FreeIntegerLimit;
            bool known = allowed.Any(a => Math.Abs(a - value) <= Math.Max(RoundingTolerance, Math.Abs(a) * 0.005));

            if (!free && !known)
            {
                violations.Add($"número que não está nos factos ({raw})");
            }
        }
    }

    private static void CheckTokens(string text, string facts, List<string> violations)
    {
        foreach (Match acronym in Acronym().Matches(text))
        {
            if (!AllowedAcronyms.Contains(acronym.Value) && !facts.Contains(acronym.Value, StringComparison.Ordinal))
            {
                violations.Add($"sigla ou ticker que não está nos factos ({acronym.Value})");
            }
        }

        foreach (Match name in MidSentenceCapitalised().Matches(text))
        {
            string word = name.Groups[1].Value;

            if (!facts.Contains(word, StringComparison.OrdinalIgnoreCase) && !AllowedAcronyms.Contains(word.ToUpperInvariant()))
            {
                violations.Add($"nome próprio que não está nos factos ({word})");
            }
        }
    }

    private static List<(double Value, bool Percent, string Raw)> Numbers(string text)
    {
        List<(double, bool, string)> numbers = [];

        foreach (Match match in Number().Matches(text))
        {
            string normalised = match.Groups["n"].Value.Replace(" ", string.Empty);
            normalised = normalised.Contains(',') && normalised.Contains('.')
                ? normalised.Replace(".", string.Empty).Replace(',', '.')
                : normalised.Replace(',', '.');

            if (double.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                numbers.Add((value, match.Groups["p"].Success, match.Value.Trim()));
            }
        }

        return numbers;
    }

    public static string StripToJson(string raw)
    {
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }

    [GeneratedRegex(@"\[(E\d+)\]")]
    private static partial Regex Citation();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"(?<![\p{L}\p{N}.])(?<n>\d{1,3}(?:[. ]\d{3})+(?:,\d+)?|\d+(?:[.,]\d+)?)(?<p>\s?%)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"\b[A-Z][A-Z0-9&]{1,6}\b")]
    private static partial Regex Acronym();

    [GeneratedRegex(@"(?<=[a-zà-ú,;:]\s)([A-ZÀ-Ú][a-zà-ú]{2,})")]
    private static partial Regex MidSentenceCapitalised();
}
