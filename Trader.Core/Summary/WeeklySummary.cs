using System.Globalization;
using Trader.Core.Calendar;
using Trader.Core.Journal;
using Trader.Core.Models;

namespace Trader.Core.Summary;

public sealed record WeeklyInput(
    IReadOnlyList<InstrumentQuote> Quotes,
    IReadOnlyList<SuggestionDto> ClosedSuggestions,
    IReadOnlyList<JournalTradeDto> ClosedTrades,
    IReadOnlyList<CalendarEvent> Events,
    DateOnly Today,
    DateTimeOffset Now);

public static class WeeklySummaryBuilder
{
    private const int TradingDays = 5;
    private const int TopMovers = 4;
    private const int MaxEvents = 8;
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static DailySummary Build(WeeklyInput input)
    {
        List<SummarySection> sections = [];
        Add(sections, "Semana no mercado", MarketLines(input.Quotes));
        Add(sections, "Sugestões fechadas na semana", SuggestionLines(input.ClosedSuggestions));
        Add(sections, "As minhas trades da semana", TradeLines(input.ClosedTrades));
        Add(sections, "Próximos 7 dias", EventLines(input.Events, input.Today));

        return new DailySummary(input.Now, Headline(input), null, sections, "automático");
    }

    public static double? WeekChange(InstrumentQuote quote)
    {
        if (quote.Sparkline.Count <= TradingDays || quote.Sparkline[^(TradingDays + 1)] <= 0)
        {
            return null;
        }

        return (quote.Sparkline[^1] - quote.Sparkline[^(TradingDays + 1)]) / quote.Sparkline[^(TradingDays + 1)] * 100;
    }

    private static string Headline(WeeklyInput input)
    {
        List<string> parts = [];
        List<double> changes = input.Quotes.Select(WeekChange).OfType<double>().ToList();

        if (changes.Count > 0)
        {
            parts.Add($"{changes.Count(c => c > 0)} de {changes.Count} ativos subiram na semana");
        }

        if (input.ClosedSuggestions.Count > 0)
        {
            parts.Add($"{input.ClosedSuggestions.Count} sugestões fechadas");
        }

        if (input.ClosedTrades.Count > 0)
        {
            parts.Add($"{input.ClosedTrades.Count} trades tuas");
        }

        return parts.Count == 0 ? "Semana sem novidades relevantes." : string.Join(" · ", parts);
    }

    private static List<string> MarketLines(IReadOnlyList<InstrumentQuote> quotes)
    {
        List<(InstrumentQuote Quote, double Change)> moved = quotes
            .Select(q => (Quote: q, Change: WeekChange(q)))
            .Where(x => x.Change is not null)
            .Select(x => (x.Quote, Change: x.Change!.Value))
            .ToList();

        if (moved.Count == 0)
        {
            return [];
        }

        return
        [
            "Maiores subidas: " + string.Join(", ", moved.OrderByDescending(x => x.Change).Take(TopMovers).Select(Format)),
            "Maiores descidas: " + string.Join(", ", moved.OrderBy(x => x.Change).Take(TopMovers).Select(Format))
        ];
    }

    private static string Format((InstrumentQuote Quote, double Change) item) =>
        $"{item.Quote.Symbol} {item.Change.ToString("+0.0;-0.0;0.0", Pt)}%";

    private static List<string> SuggestionLines(IReadOnlyList<SuggestionDto> closed)
    {
        List<SuggestionDto> withResult = closed.Where(s => s.RMultiple is not null).ToList();

        if (withResult.Count == 0)
        {
            return [];
        }

        int wins = withResult.Count(s => s.RMultiple > 0);
        double total = withResult.Sum(s => s.RMultiple!.Value);
        SuggestionDto best = withResult.MaxBy(s => s.RMultiple)!;
        SuggestionDto worst = withResult.MinBy(s => s.RMultiple)!;

        return
        [
            $"{withResult.Count} fechadas, {wins} com lucro ({(100.0 * wins / withResult.Count).ToString("0", Pt)}%), {total.ToString("+0.0;-0.0;0.0", Pt)} R no total",
            $"Melhor: {best.Symbol} {best.RMultiple!.Value.ToString("+0.00;-0.00", Pt)} R · Pior: {worst.Symbol} {worst.RMultiple!.Value.ToString("+0.00;-0.00", Pt)} R"
        ];
    }

    private static List<string> TradeLines(IReadOnlyList<JournalTradeDto> trades)
    {
        if (trades.Count == 0)
        {
            return [];
        }

        int wins = trades.Count(t => t.NetPnl > 0);
        double pnl = trades.Sum(t => t.NetPnl ?? 0);
        double r = trades.Sum(t => t.RMultiple ?? 0);

        return
        [
            $"{trades.Count} fechadas, {wins} com lucro ({(100.0 * wins / trades.Count).ToString("0", Pt)}%)",
            $"Resultado líquido {pnl.ToString("+#,0.00;-#,0.00;0.00", Pt)} · {r.ToString("+0.0;-0.0;0.0", Pt)} R no total"
        ];
    }

    private static List<string> EventLines(IReadOnlyList<CalendarEvent> events, DateOnly today) =>
        events
            .Where(e => e.Date > today && e.Date <= today.AddDays(7))
            .OrderBy(e => e.Date)
            .Take(MaxEvents)
            .Select(e => $"{CalendarAlerts.DayLabel(e.Date, today)}: {e.Title}{(e.Detail.Length > 0 ? " (" + e.Detail + ")" : string.Empty)}")
            .ToList();

    private static void Add(List<SummarySection> sections, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count > 0)
        {
            sections.Add(new SummarySection(title, lines));
        }
    }
}
