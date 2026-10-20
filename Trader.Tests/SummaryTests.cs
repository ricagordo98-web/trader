using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Trader.Core.Calendar;
using Trader.Core.Models;
using Trader.Core.Summary;
using Trader.Infrastructure.Summary;

namespace Trader.Tests;

public class SummaryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 1, 5);

    private static InstrumentQuote Quote(string symbol, double? change) => new(symbol, symbol, AssetClass.Stock, "USD", 100, 1, change, []);

    private static SuggestionDto Suggestion(string symbol, TradeDirection direction, double confidence, double? r = null) => new(
        1, symbol, symbol, direction, confidence, 100, 98, 105, 2.5, string.Empty, Now, Now.AddDays(5),
        r is null ? SuggestionStatus.Open : SuggestionStatus.HitTakeProfit, r is null ? null : Now, null, r, [], []);

    private static SummaryInput Input(
        IReadOnlyList<InstrumentQuote>? quotes = null,
        IReadOnlyList<SentimentSummary>? sentiment = null,
        IReadOnlyList<SuggestionDto>? created = null,
        IReadOnlyList<SuggestionDto>? closed = null,
        IReadOnlyList<HeadlineItem>? headlines = null,
        IReadOnlyList<CalendarEvent>? events = null) =>
        new(quotes ?? [], sentiment ?? [], created ?? [], closed ?? [], headlines ?? [], events ?? [], Today, Now);

    [Fact]
    public void Build_NoData_ReturnsQuietHeadlineAndNoSections()
    {
        DailySummary summary = SummaryBuilder.Build(Input());

        summary.Headline.Should().Be("Sem novidades relevantes.");
        summary.Sections.Should().BeEmpty();
        summary.Source.Should().Be("automático");
    }

    [Fact]
    public void Build_Market_ListsTopMoversBothWays()
    {
        DailySummary summary = SummaryBuilder.Build(Input(quotes:
        [
            Quote("AAA", 3.2), Quote("BBB", 1.0), Quote("CCC", -0.5), Quote("DDD", -4.1), Quote("EEE", 0.2), Quote("FFF", null)
        ]));

        SummarySection market = summary.Sections.Single(s => s.Title == "Mercado");
        market.Lines[0].Should().StartWith("Maiores subidas: AAA +3,20%, BBB +1,00%, EEE +0,20%");
        market.Lines[1].Should().StartWith("Maiores descidas: DDD -4,10%, CCC -0,50%");
        summary.Headline.Should().Contain("3 de 5 ativos sobem");
    }

    [Fact]
    public void Build_Sentiment_IgnoresSparseCoverage()
    {
        DailySummary summary = SummaryBuilder.Build(Input(sentiment:
        [
            new SentimentSummary("AAA", "AAA", 10, 0.6),
            new SentimentSummary("BBB", "BBB", 2, 0.9),
            new SentimentSummary("CCC", "CCC", 7, -0.4)
        ]));

        SummarySection section = summary.Sections.Single(s => s.Title.StartsWith("Sentimento"));
        section.Lines.Should().Contain(l => l.StartsWith("Mais positivo: AAA") && !l.Contains("BBB"));
        section.Lines.Should().Contain(l => l.StartsWith("Mais negativo: CCC"));
    }

    [Fact]
    public void Build_Suggestions_DescribesNewAndFinishedOnes()
    {
        DailySummary summary = SummaryBuilder.Build(Input(
            created: [Suggestion("AAA", TradeDirection.Buy, 0.72), Suggestion("BBB", TradeDirection.Sell, 0.65)],
            closed: [Suggestion("CCC", TradeDirection.Buy, 0.7, 2.5)]));

        SummarySection section = summary.Sections.Single(s => s.Title == "Sugestões");
        section.Lines.Should().Contain("Nova: COMPRAR AAA (72% de certeza)");
        section.Lines.Should().Contain("Terminada: CCC com +2,50 R");
        summary.Headline.Should().Contain("2 sugestões novas");
    }

    [Fact]
    public void Build_Events_OnlyTodayAndTomorrow()
    {
        DailySummary summary = SummaryBuilder.Build(Input(events:
        [
            new CalendarEvent(Today, CalendarKind.Earnings, "AAA", "Resultados de AAA", "antes da abertura"),
            new CalendarEvent(Today.AddDays(1), CalendarKind.Macro, null, "Inflação (CPI, EUA)", string.Empty),
            new CalendarEvent(Today.AddDays(5), CalendarKind.Macro, null, "PIB (EUA)", string.Empty)
        ]));

        SummarySection section = summary.Sections.Single(s => s.Title == "Hoje e amanhã");
        section.Lines.Should().Equal("Hoje: Resultados de AAA (antes da abertura)", "Amanhã: Inflação (CPI, EUA)");
        summary.Headline.Should().Contain("1 eventos amanhã");
    }

    [Fact]
    public void Prompt_ContainsInstructionsAndTheData()
    {
        DailySummary summary = SummaryBuilder.Build(Input(quotes: [Quote("AAA", 2)]));

        string prompt = SummaryBuilder.Prompt(summary);

        prompt.Should().Contain("português de Portugal").And.Contain("Maiores subidas: AAA");
    }
}

public class SummaryScheduleTests
{
    [Fact]
    public void IsDue_BeforeGenerateHour_ReturnsFalse()
    {
        DateTimeOffset early = new DateTimeOffset(DateTime.Today.AddHours(6), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));

        SummarySchedule.IsDue(null, early, 8).Should().BeFalse();
    }

    [Fact]
    public void IsDue_AfterHourWithNoSummaryToday_ReturnsTrue()
    {
        DateTimeOffset now = new DateTimeOffset(DateTime.Today.AddHours(9), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));

        SummarySchedule.IsDue(null, now, 8).Should().BeTrue();
        SummarySchedule.IsDue(now.AddDays(-1), now, 8).Should().BeTrue();
    }

    [Fact]
    public void IsDue_AlreadyGeneratedToday_ReturnsFalse()
    {
        DateTimeOffset now = new DateTimeOffset(DateTime.Today.AddHours(15), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));

        SummarySchedule.IsDue(now.AddHours(-5), now, 8).Should().BeFalse();
    }
}

public class SummaryIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(TraderFactory factory)
    {
        HttpClient client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { email = "a@example.com", password = Password })).EnsureSuccessStatusCode();
        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email = "a@example.com", password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Summary_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().GetAsync("/api/summary")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Summary_BeforeGeneration_ReturnsNoContent()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory);

        (await client.GetAsync("/api/summary")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
