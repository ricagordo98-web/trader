using System.Globalization;
using Trader.Core.Calendar;
using Trader.Core.Models;

namespace Trader.Core.Summary;

public sealed record SummarySection(string Title, IReadOnlyList<string> Lines);

public sealed record DailySummary(
    DateTimeOffset GeneratedAt,
    string Headline,
    string? Narrative,
    IReadOnlyList<SummarySection> Sections,
    string Source);

public sealed record MoverReason(string Symbol, double ChangePercent, string Text);

public sealed record HeadlineItem(string Title, string Source, double? Sentiment, IReadOnlyList<string> Symbols);

public sealed record SummaryInput(
    IReadOnlyList<InstrumentQuote> Quotes,
    IReadOnlyList<SentimentSummary> Sentiment,
    IReadOnlyList<SuggestionDto> NewSuggestions,
    IReadOnlyList<SuggestionDto> ClosedSuggestions,
    IReadOnlyList<HeadlineItem> Headlines,
    IReadOnlyList<CalendarEvent> Events,
    DateOnly Today,
    DateTimeOffset Now,
    IReadOnlyList<MoverReason>? Reasons = null);

public static class SummaryBuilder
{
    private const int TopMovers = 3;
    private const int TopHeadlines = 5;
    private const int MinArticlesForSentiment = 3;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static DailySummary Build(SummaryInput input)
    {
        List<SummarySection> sections = [];

        AddSection(sections, "Mercado", MarketLines(input.Quotes));
        AddSection(sections, "Porque se mexeram", (input.Reasons ?? []).Select(r => $"{r.Symbol} {r.ChangePercent.ToString("+0.00;-0.00;0.00", Pt)}%: {r.Text}").ToList());
        AddSection(sections, "Sentimento (24 h)", SentimentLines(input.Sentiment));
        AddSection(sections, "Sugestões", SuggestionLines(input.NewSuggestions, input.ClosedSuggestions));
        AddSection(sections, "Hoje e amanhã", EventLines(input.Events, input.Today));
        AddSection(sections, "Notícias em destaque", input.Headlines
            .Take(TopHeadlines)
            .Select(h => $"{h.Title} ({h.Source}{(h.Symbols.Count > 0 ? ", " + string.Join("/", h.Symbols.Take(3)) : string.Empty)})")
            .ToList());

        return new DailySummary(input.Now, Headline(input), null, sections, "automático");
    }

    public static string Prompt(DailySummary summary)
    {
        string body = string.Join("\n\n", summary.Sections.Select(s => $"{s.Title}:\n" + string.Join("\n", s.Lines.Select(l => "- " + l))));

        return "És um assistente que resume o dia de mercado para um investidor particular. "
            + "Escreve em português de Portugal, com 5 a 7 frases, tom sóbrio, sem conselhos financeiros e sem inventar números além dos dados abaixo. "
            + "Destaca o que mudou, o sentimento, as sugestões e os eventos de hoje e amanhã.\n\n"
            + body;
    }

    private static string Headline(SummaryInput input)
    {
        List<string> parts = [];
        InstrumentQuote[] withChange = input.Quotes.Where(q => q.ChangePercent is not null).ToArray();

        if (withChange.Length > 0)
        {
            int up = withChange.Count(q => q.ChangePercent > 0);
            parts.Add($"{up} de {withChange.Length} ativos sobem");
        }

        if (input.NewSuggestions.Count > 0)
        {
            parts.Add($"{input.NewSuggestions.Count} sugestões novas");
        }

        int tomorrow = input.Events.Count(e => e.Date == input.Today.AddDays(1));

        if (tomorrow > 0)
        {
            parts.Add($"{tomorrow} eventos amanhã");
        }

        return parts.Count == 0 ? "Sem novidades relevantes." : string.Join(" · ", parts);
    }

    private static List<string> MarketLines(IReadOnlyList<InstrumentQuote> quotes)
    {
        List<InstrumentQuote> moved = quotes.Where(q => q.ChangePercent is not null).ToList();

        if (moved.Count == 0)
        {
            return [];
        }

        List<string> lines = [];
        lines.Add("Maiores subidas: " + string.Join(", ", moved.OrderByDescending(q => q.ChangePercent).Take(TopMovers).Select(Move)));
        lines.Add("Maiores descidas: " + string.Join(", ", moved.OrderBy(q => q.ChangePercent).Take(TopMovers).Select(Move)));
        return lines;
    }

    private static string Move(InstrumentQuote quote) =>
        $"{quote.Symbol} {quote.ChangePercent!.Value.ToString("+0.00;-0.00;0.00", Pt)}%";

    private static List<string> SentimentLines(IReadOnlyList<SentimentSummary> sentiment)
    {
        List<SentimentSummary> covered = sentiment.Where(s => s.ArticleCount >= MinArticlesForSentiment).ToList();

        if (covered.Count == 0)
        {
            return [];
        }

        List<string> lines = [];
        List<SentimentSummary> positive = covered.Where(s => SentimentBands.IsPositive(s.AverageSentiment)).OrderByDescending(s => s.AverageSentiment).Take(TopMovers).ToList();
        List<SentimentSummary> negative = covered.Where(s => SentimentBands.IsNegative(s.AverageSentiment)).OrderBy(s => s.AverageSentiment).Take(TopMovers).ToList();

        if (positive.Count > 0)
        {
            lines.Add("Mais positivo: " + string.Join(", ", positive.Select(Sentiment)));
        }

        if (negative.Count > 0)
        {
            lines.Add("Mais negativo: " + string.Join(", ", negative.Select(Sentiment)));
        }

        if (lines.Count == 0)
        {
            lines.Add("Sentimento sem extremos: nenhum ativo com notícias claramente positivas ou negativas.");
        }

        return lines;
    }

    private static string Sentiment(SentimentSummary summary) =>
        $"{summary.Symbol} {summary.AverageSentiment.ToString("+0.00;-0.00;0.00", Pt)} ({summary.ArticleCount} notícias)";

    private static List<string> SuggestionLines(IReadOnlyList<SuggestionDto> created, IReadOnlyList<SuggestionDto> closed)
    {
        List<string> lines = [];

        foreach (SuggestionDto suggestion in created.OrderByDescending(s => s.Confidence).Take(5))
        {
            lines.Add($"Nova: {(suggestion.Direction == TradeDirection.Buy ? "COMPRAR" : "VENDER")} {suggestion.Symbol} ({(suggestion.Confidence * 100).ToString("0", Pt)}% de certeza)");
        }

        foreach (SuggestionDto suggestion in closed.Where(s => s.RMultiple is not null).Take(5))
        {
            lines.Add($"Terminada: {suggestion.Symbol} com {suggestion.RMultiple!.Value.ToString("+0.00;-0.00;0.00", Pt)} R");
        }

        return lines;
    }

    private static List<string> EventLines(IReadOnlyList<CalendarEvent> events, DateOnly today) =>
        events
            .Where(e => e.Date >= today && e.Date <= today.AddDays(1))
            .OrderBy(e => e.Date)
            .Select(e => $"{CalendarAlerts.DayLabel(e.Date, today)}: {e.Title}{(e.Detail.Length > 0 ? " (" + e.Detail + ")" : string.Empty)}")
            .ToList();

    private static void AddSection(List<SummarySection> sections, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count > 0)
        {
            sections.Add(new SummarySection(title, lines));
        }
    }
}
