using FluentAssertions;
using Trader.Core.Calendar;
using Trader.Core.Journal;
using Trader.Core.Models;
using Trader.Core.Summary;

namespace Trader.Tests;

public class WeeklySummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 24);

    private static InstrumentQuote Quote(string symbol, double start, double end) =>
        new(symbol, symbol, AssetClass.Stock, "USD", end, 0, 0, [start, start, start, start, start, start, end]);

    private static SuggestionDto Suggestion(string symbol, double r) =>
        new(1, symbol, symbol, TradeDirection.Buy, 0.7, 100, 98, 104, 2, "x", Now.AddDays(-3), Now, r > 0 ? SuggestionStatus.HitTakeProfit : SuggestionStatus.HitStopLoss, Now.AddDays(-1), 100, r, [], []);

    private static JournalTradeDto Trade(double pnl, double r) =>
        new(1, "AAA", "AAA", TradeDirection.Buy, Now.AddDays(-4), 100, 1, 98, 104, 1, 0, "XTB", string.Empty, null, TradeStatus.Closed, Now.AddDays(-2), 101, 2, pnl, r, null, null);

    [Fact]
    public void WeekChange_UsesTheLastFiveSessions()
    {
        WeeklySummaryBuilder.WeekChange(Quote("A", 100, 110)).Should().BeApproximately(10, 1e-9);
        WeeklySummaryBuilder.WeekChange(new InstrumentQuote("B", "B", AssetClass.Stock, "USD", 1, 0, 0, [1, 2, 3])).Should().BeNull();
    }

    [Fact]
    public void Build_SummarisesMarketSuggestionsTradesAndUpcomingEvents()
    {
        WeeklyInput input = new(
            [Quote("UP", 100, 112), Quote("DOWN", 100, 91), Quote("FLAT", 100, 100)],
            [Suggestion("WIN", 2), Suggestion("LOSE", -1), Suggestion("WIN2", 1.5)],
            [Trade(50, 1), Trade(-25, -0.5)],
            [
                new CalendarEvent(Today.AddDays(2), CalendarKind.Macro, null, "Inflação (CPI, EUA)", string.Empty),
                new CalendarEvent(Today.AddDays(20), CalendarKind.Macro, null, "Fora de alcance", string.Empty)
            ],
            Today,
            Now);

        DailySummary summary = WeeklySummaryBuilder.Build(input);

        summary.Headline.Should().Contain("1 de 3 ativos subiram").And.Contain("3 sugestões fechadas").And.Contain("2 trades tuas");
        summary.Sections.Select(s => s.Title).Should().Equal("Semana no mercado", "Sugestões fechadas na semana", "As minhas trades da semana", "Próximos 7 dias");
        summary.Sections[0].Lines[0].Should().Contain("UP +12,0%");
        summary.Sections[0].Lines[1].Should().Contain("DOWN -9,0%");
        summary.Sections[1].Lines[0].Should().Contain("3 fechadas, 2 com lucro (67%)").And.Contain("+2,5 R");
        summary.Sections[1].Lines[1].Should().Contain("Melhor: WIN").And.Contain("Pior: LOSE");
        summary.Sections[2].Lines[1].Should().Contain("+25,00").And.Contain("+0,5 R");
        summary.Sections[3].Lines.Should().ContainSingle().Which.Should().Contain("Inflação");
    }

    [Fact]
    public void Build_WithNothingToReport_SaysSo()
    {
        DailySummary summary = WeeklySummaryBuilder.Build(new WeeklyInput([], [], [], [], Today, Now));

        summary.Headline.Should().Be("Semana sem novidades relevantes.");
        summary.Sections.Should().BeEmpty();
    }
}
