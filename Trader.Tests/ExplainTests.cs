using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Explain;
using Trader.Core.Models;
using Trader.Infrastructure.Explain;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

internal static class ExplainFixtures
{
    public static readonly DateTimeOffset Peak = new(2026, 10, 20, 14, 30, 0, TimeSpan.Zero);

    public static List<Candle> GapDown(double gap = 0.94)
    {
        List<Candle> candles = IntradayFixtures.Day(new DateTime(2026, 10, 19), 100, 78, i => 100 + 0.01 * (i % 5), 1000);
        candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 20), 100 * gap, 30, i => 100 * gap - 0.05 * i, 3500));
        return candles;
    }

    public static List<Candle> Flat(DateTime day) => IntradayFixtures.Day(day, 4000, 78, i => 4000 + (i % 3), 1000);

    public static MoveAnalysis Analysis(IReadOnlyList<Candle>? series = null, BenchmarkInput? benchmark = null, IReadOnlyList<PeerMove>? peers = null)
    {
        IReadOnlyList<Candle> candles = series ?? GapDown();
        DateTimeOffset from = Peak;
        DateTimeOffset to = candles.Max(c => c.Time);
        return MoveAnalyzer.Analyze("TEST", "Test Inc", "5m", candles, from, to, benchmark, peers ?? [])!;
    }

    public static EvidenceItem Earnings(DateTimeOffset time) => new(
        EvidenceKind.Earnings, CauseCategory.Earnings, "Test Inc divulgou resultados: EPS 1,10 vs 1,40 esperado", "depois do fecho; surpresa de EPS -21,4%",
        "https://example.test/earnings", "Finnhub", time, 1.3, -0.5, true);

    public static EvidenceItem News(string title, DateTimeOffset time, double reliability = 1.0, bool mentions = true, double? sentiment = null) =>
        new(EvidenceKind.Article, CauseClassifier.Categorize(title), title, string.Empty, $"https://example.test/{Math.Abs(title.GetHashCode())}", "Reuters", time, reliability, sentiment, mentions);
}

public class MoveAnalyzerTests
{
    [Fact]
    public void Analyze_GapDown_MeasuresMoveGapAndPeak()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();

        analysis.ChangePercent.Should().BeLessThan(-6);
        analysis.GapPercent.Should().BeApproximately(-6, 0.3);
        analysis.PeakTime.Should().Be(ExplainFixtures.Peak);
        analysis.PeakShare.Should().BeGreaterThan(0.8);
        analysis.ZScore.Should().BeLessThan(-3);
        analysis.VolumeRatio.Should().BeGreaterThan(3);
    }

    [Fact]
    public void Analyze_MoveInsideNormalRange_IsSmall()
    {
        List<Candle> candles = IntradayFixtures.Day(new DateTime(2026, 10, 19), 100, 78, i => 100 + 0.2 * Math.Sin(i), 1000);
        candles.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 20), 100, 20, i => 100 + 0.2 * Math.Sin(i), 1000));

        MoveAnalyzer.Analyze("TEST", "Test", "5m", candles, candles[78].Time, candles[^1].Time, null, [])!.Scope.Should().Be(MoveScope.Small);
    }

    [Fact]
    public void Analyze_MarketDropExplainsMove_IsMarketWide()
    {
        List<Candle> candles = ExplainFixtures.GapDown();
        List<Candle> index = ExplainFixtures.Flat(new DateTime(2026, 10, 19));
        index.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 20), 4000 * 0.94, 30, i => 4000 * 0.94 - 0.5 * i, 1000));

        MoveAnalysis analysis = ExplainFixtures.Analysis(candles, new BenchmarkInput("^GSPC", "S&P 500", index, 1.0));

        analysis.Scope.Should().Be(MoveScope.MarketWide);
        analysis.Benchmark!.ChangePercent.Should().BeLessThan(-5);
    }

    [Fact]
    public void Analyze_FlatMarket_IsCompanySpecific()
    {
        List<Candle> index = ExplainFixtures.Flat(new DateTime(2026, 10, 19));
        index.AddRange(ExplainFixtures.Flat(new DateTime(2026, 10, 20)).Take(30));

        MoveAnalysis analysis = ExplainFixtures.Analysis(null, new BenchmarkInput("^GSPC", "S&P 500", index, 1.0));

        analysis.Scope.Should().Be(MoveScope.CompanySpecific);
        analysis.ResidualPercent.Should().BeLessThan(-5);
    }

    [Fact]
    public void Analyze_CorrelatedPeersMovedTheSame_IsSector()
    {
        IReadOnlyList<PeerMove> peers = [new("AMD", "AMD", -5.5, 0.9), new("NVDA", "Nvidia", -6.2, 0.85)];

        ExplainFixtures.Analysis(null, null, peers).Scope.Should().Be(MoveScope.Sector);
    }

    [Fact]
    public void Analyze_WindowWithoutCandles_ReturnsNull() =>
        MoveAnalyzer.Analyze("TEST", "Test", "5m", ExplainFixtures.GapDown(), new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), null, []).Should().BeNull();
}

public class CauseClassifierTests
{
    [Theory]
    [InlineData("Netflix tops earnings estimates as revenue climbs", CauseCategory.Earnings)]
    [InlineData("Apple cuts full-year forecast on weak demand", CauseCategory.Guidance)]
    [InlineData("Goldman downgrades Tesla to sell, lowers price target", CauseCategory.Analyst)]
    [InlineData("Broadcom to acquire rival in $10 billion deal", CauseCategory.MergerAcquisition)]
    [InlineData("Intel CEO steps down after board dispute", CauseCategory.Management)]
    [InlineData("Boeing faces FAA investigation after incident", CauseCategory.Legal)]
    [InlineData("Microsoft unveils new AI product line", CauseCategory.Product)]
    [InlineData("Coca-Cola raises dividend by 5%", CauseCategory.Dividend)]
    [InlineData("Fed signals rate cut as inflation cools", CauseCategory.Macro)]
    [InlineData("Local bakery opens a second shop", CauseCategory.Other)]
    public void Categorize_Headlines_PicksTheRightCategory(string title, CauseCategory expected) =>
        CauseClassifier.Categorize(title).Should().Be(expected);
}

public class CauseRankerTests
{
    [Fact]
    public void Rank_EarningsTheEveningBefore_BeatsUnrelatedAndLateNews()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();
        DateTimeOffset evening = analysis.PeakTime.AddHours(-17.5);
        IReadOnlyList<EvidenceItem> evidence =
        [
            ExplainFixtures.News("Local bakery opens a second shop", analysis.PeakTime.AddMinutes(-20), 1.0, false),
            ExplainFixtures.Earnings(evening),
            ExplainFixtures.News("Test Inc shares slide after weak results", analysis.PeakTime.AddHours(-17), 1.0, true, -0.7),
            ExplainFixtures.News("Analyst reiterates view on Test Inc", analysis.PeakTime.AddHours(30), 1.0, true)
        ];

        IReadOnlyList<MoveCause> causes = CauseRanker.Rank(analysis, evidence);

        causes[0].Kind.Should().Be(EvidenceKind.Earnings);
        causes[0].Id.Should().Be("E1");
        causes.Select(c => c.Title).Should().NotContain(t => t.Contains("bakery"));
        causes.Should().OnlyContain(c => c.Score >= 0.45);
    }

    [Fact]
    public void Rank_NearDuplicateHeadlines_AreMergedAndCounted()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();
        IReadOnlyList<EvidenceItem> evidence =
        [
            ExplainFixtures.News("Test Inc plunges as CEO resigns unexpectedly", analysis.PeakTime.AddMinutes(-30)),
            ExplainFixtures.News("Test Inc plunges as CEO resigns unexpectedly - report", analysis.PeakTime.AddMinutes(-25)),
            ExplainFixtures.News("Test Inc plunges after CEO resigns unexpectedly", analysis.PeakTime.AddMinutes(-10))
        ];

        IReadOnlyList<MoveCause> causes = CauseRanker.Rank(analysis, evidence);

        causes.Should().ContainSingle();
        causes[0].Duplicates.Should().Be(2);
        causes[0].Category.Should().Be(CauseCategory.Management);
    }

    [Fact]
    public void Rank_NewsOppositeToTheMove_ScoresLower()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();
        EvidenceItem same = ExplainFixtures.News("Test Inc warns on profit", analysis.PeakTime.AddMinutes(-30), 1.0, true, -0.8);
        EvidenceItem opposite = ExplainFixtures.News("Test Inc wins big contract", analysis.PeakTime.AddMinutes(-30), 1.0, true, 0.8);

        double scoreSame = CauseRanker.Rank(analysis, [same])[0].Score;
        double scoreOpposite = CauseRanker.Rank(analysis, [opposite])[0].Score;

        scoreSame.Should().BeGreaterThan(scoreOpposite);
    }

    [Fact]
    public void Rank_ArticleThatDoesNotMentionTheAsset_IsDropped()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();

        CauseRanker.Rank(analysis, [ExplainFixtures.News("Rival company posts quarterly earnings beat", analysis.PeakTime.AddMinutes(-20), 1.0, false)])
            .Should().BeEmpty();
    }

    [Fact]
    public void Rank_NewsCloserToThePeakOutranksOlderNews()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();
        EvidenceItem older = ExplainFixtures.News("Test Inc explores merger with larger rival", analysis.PeakTime.AddMinutes(-115));
        EvidenceItem closer = ExplainFixtures.News("Test Inc confirms takeover talks with larger rival", analysis.PeakTime.AddMinutes(-20));

        CauseRanker.Rank(analysis, [older, closer])[0].Title.Should().Be(closer.Title);
    }

    [Fact]
    public void Rank_HeadlineDescribingTheSameDirectionOfMove_OutranksOppositeDirection()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();
        EvidenceItem same = ExplainFixtures.News("Test Inc stock plunges after CEO resigns", analysis.PeakTime.AddMinutes(-30));
        EvidenceItem opposite = ExplainFixtures.News("Test Inc stock jumps after CEO resigns", analysis.PeakTime.AddMinutes(-30));

        CauseRanker.Rank(analysis, [same])[0].Score.Should().BeGreaterThan(CauseRanker.Rank(analysis, [opposite])[0].Score);
    }

    [Fact]
    public void Rank_NothingRelevant_ReturnsNoCauses() =>
        CauseRanker.Rank(ExplainFixtures.Analysis(), [ExplainFixtures.News("Local bakery opens a second shop", ExplainFixtures.Peak.AddDays(-9), 0.9, false)])
            .Should().BeEmpty();

    [Fact]
    public void Rank_MarketWideMove_CapsConfidenceAtMedium()
    {
        List<Candle> index = ExplainFixtures.Flat(new DateTime(2026, 10, 19));
        index.AddRange(IntradayFixtures.Day(new DateTime(2026, 10, 20), 4000 * 0.94, 30, i => 4000 * 0.94 - 0.5 * i, 1000));
        MoveAnalysis analysis = ExplainFixtures.Analysis(null, new BenchmarkInput("^GSPC", "S&P 500", index, 1.0));

        IReadOnlyList<MoveCause> causes = CauseRanker.Rank(analysis, [ExplainFixtures.Earnings(analysis.PeakTime.AddHours(-17.5))]);

        causes[0].Confidence.Should().NotBe("Alta");
    }
}

public class ExplanationTextTests
{
    [Fact]
    public void Summary_CompanySpecificWithCause_NamesTheCauseAndAdmitsUncertainty()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();
        IReadOnlyList<MoveCause> causes = CauseRanker.Rank(analysis, [ExplainFixtures.Earnings(analysis.PeakTime.AddHours(-17.5))]);

        string text = ExplanationText.Summary(analysis, causes);

        text.Should().Contain("TEST desceu").And.Contain("resultados").And.Contain("não provada");
    }

    [Fact]
    public void Summary_NoEvidence_SaysSo()
    {
        MoveAnalysis analysis = ExplainFixtures.Analysis();

        ExplanationText.Summary(analysis, []).Should().Contain("Não encontrei notícias");
    }
}

public class NarrationGuardTests
{
    private static readonly MoveAnalysis Analysis = ExplainFixtures.Analysis();
    private static readonly IReadOnlyList<MoveCause> Causes = CauseRanker.Rank(Analysis, [ExplainFixtures.Earnings(Analysis.PeakTime.AddHours(-17.5))]);
    private static readonly string Facts = NarrationPrompt.Facts(Analysis, Causes);
    private static readonly IReadOnlyCollection<string> Ids = Causes.Select(c => c.Id).ToList();

    private static IReadOnlyList<string> Check(string summary, string confidence = "media", params string[] used) =>
        NarrationGuard.Validate(new NarrationDraft(summary, confidence, used), Facts, Ids);

    [Fact]
    public void Validate_FaithfulText_Passes() =>
        Check("A queda de cerca de 6% deve-se provavelmente aos resultados divulgados, com EPS 1,10 vs 1,40 esperado [E1].", "media", "E1").Should().BeEmpty();

    [Fact]
    public void Validate_InventedNumber_IsRejected() =>
        Check("A ação caiu 12% por causa dos resultados [E1].", "media", "E1").Should().Contain(v => v.Contains("12"));

    [Fact]
    public void Validate_InventedCompanyOrPerson_IsRejected() =>
        Check("O CEO Tim Cook anunciou uma investigação da Microsoft [E1].", "media", "E1").Should().NotBeEmpty();

    [Fact]
    public void Validate_InventedTicker_IsRejected() =>
        Check("A queda acompanha a NVDA [E1].", "media", "E1").Should().Contain(v => v.Contains("NVDA"));

    [Fact]
    public void Validate_CitesMissingEvidence_IsRejected() =>
        Check("Provavelmente devido aos resultados [E7].", "media", "E7").Should().Contain(v => v.Contains("E7"));

    [Fact]
    public void Validate_CauseWithoutCitation_IsRejected() =>
        Check("Provavelmente devido aos resultados.", "media").Should().Contain(v => v.Contains("sem citar"));

    [Fact]
    public void Validate_InvestmentAdvice_IsRejected() =>
        Check("Recomendo vender antes da próxima queda [E1].", "media", "E1").Should().Contain(v => v.Contains("aconselhamento"));

    [Fact]
    public void Validate_UnknownUrl_IsRejected() =>
        Check("Ver https://evil.example/x [E1].", "media", "E1").Should().Contain(v => v.Contains("endereço"));

    [Fact]
    public void Validate_NoCauseAdmitted_PassesWithoutCitation() =>
        NarrationGuard.Validate(new NarrationDraft("Não há causa identificada nos dados disponíveis.", "nenhuma", []), Facts, []).Should().BeEmpty();

    [Fact]
    public void Validate_RoundedNumberWithinTolerance_Passes()
    {
        string facts = "Movimento: desceu 6,64% entre 2026-10-20 14:30 e 2026-10-20 16:55 UTC";

        NarrationGuard.Validate(new NarrationDraft("Desceu cerca de 6,6% [E1].", "media", ["E1"]), facts, ["E1"]).Should().BeEmpty();
    }
}

public class EvidenceParsingTests
{
    private static readonly EvidenceQuery Query = new("NFLX", "Netflix", AssetClass.Stock, new DateTimeOffset(2026, 10, 20, 13, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero));

    [Fact]
    public void EarningsParse_ComputesSurpriseAndPlacesTheEventAfterTheClose()
    {
        const string json = """{"earningsCalendar":[{"date":"2026-10-19","epsActual":5.1,"epsEstimate":5.45,"hour":"amc","revenueActual":11.5,"revenueEstimate":11.2,"symbol":"NFLX"}]}""";

        EvidenceItem item = EarningsEvidence.Parse(Query, json).Single();

        item.Kind.Should().Be(EvidenceKind.Earnings);
        item.Time.Should().Be(new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero));
        item.Title.Should().Contain("EPS 5,10 vs 5,45");
        item.Detail.Should().Contain("surpresa de EPS -6,4%").And.Contain("receitas +2,7%");
        item.Sentiment.Should().BeLessThan(0);
    }

    [Fact]
    public void EarningsParse_FutureEventWithoutActuals_IsIgnored() =>
        EarningsEvidence.Parse(Query, """{"earningsCalendar":[{"date":"2026-10-30","epsEstimate":5.45,"hour":"amc"}]}""").Should().BeEmpty();

    [Fact]
    public void EdgarParse_8KItems_AreTranslatedAndInsidersLimited()
    {
        const string json = """
            {"filings":{"recent":{
              "form":["8-K","4","4","4","10-Q"],
              "acceptanceDateTime":["2026-10-20T13:05:00.000Z","2026-10-20T12:00:00.000Z","2026-10-20T11:00:00.000Z","2026-10-20T10:00:00.000Z","2026-10-20T09:00:00.000Z"],
              "accessionNumber":["0001-26-000001","0001-26-000002","0001-26-000003","0001-26-000004","0001-26-000005"],
              "primaryDocument":["a.htm","b.xml","c.xml","d.xml","e.htm"],
              "items":["5.02,9.01","","","",""],
              "primaryDocDescription":["8-K","","","",""]}}}
            """;

        IReadOnlyList<EvidenceItem> items = EdgarEvidence.Parse(Query, "0001065280", json);

        items.Should().HaveCount(3);
        items[0].Category.Should().Be(CauseCategory.Management);
        items[0].Detail.Should().Contain("saída ou nomeação");
        items[0].Url.Should().Be("https://www.sec.gov/Archives/edgar/data/1065280/000126000001/a.htm");
        items.Count(i => i.Category == CauseCategory.Insider).Should().Be(2);
    }

    [Fact]
    public void NarratorParse_AcceptsJsonWrappedInTextAndNormalisesFields()
    {
        NarrationDraft? draft = OllamaMoveNarrator.Parse("Aqui vai: {\"summary\":\"Texto [E1].\",\"confidence\":\"Média\",\"used\":[\"[E1]\"]}");

        draft.Should().NotBeNull();
        draft!.Confidence.Should().Be("media");
        draft.Used.Should().Equal("E1");
        OllamaMoveNarrator.Parse("isto não é json").Should().BeNull();
    }
}

public class MoveExplainerTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IMarketDataService _marketData = Substitute.For<IMarketDataService>();
    private readonly IMoveNarrator _narrator = Substitute.For<IMoveNarrator>();
    private readonly IEvidenceSource _source = Substitute.For<IEvidenceSource>();
    private TraderDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
        _dbContext.Instruments.Add(new Instrument { Symbol = "TESTX", Name = "Test Inc", AssetClass = AssetClass.Stock, Currency = "USD", SortOrder = 9999 });
        await _dbContext.SaveChangesAsync();

        List<Candle> flat = ExplainFixtures.Flat(new DateTime(2026, 10, 19));
        flat.AddRange(ExplainFixtures.Flat(new DateTime(2026, 10, 20)).Take(30));
        _marketData.GetCandlesAsync("TESTX", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ExplainFixtures.GapDown());
        _marketData.GetCandlesAsync("^GSPC", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(flat);
        _marketData.GetQuotesAsync(Arg.Any<AssetClass?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InstrumentQuote>());
        _source.Name.Returns("Fake");
        _source.FetchAsync(Arg.Any<EvidenceQuery>(), Arg.Any<CancellationToken>()).Returns([ExplainFixtures.Earnings(ExplainFixtures.Peak.AddHours(-17.5))]);
        _narrator.IsEnabled.Returns(true);
        _narrator.NarrateAsync(Arg.Any<MoveAnalysis>(), Arg.Any<IReadOnlyList<MoveCause>>(), Arg.Any<CancellationToken>())
            .Returns(new NarrationResult("Provável efeito dos resultados [E1].", NarrativeStatus.Accepted, "modelo local (teste)", []));
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private MoveExplainer CreateExplainer() => new(_dbContext, _marketData, [_source], _narrator, Substitute.For<ISentimentAnalyzer>(), NullLogger<MoveExplainer>.Instance);

    private static MoveRequest Request(bool refresh = false) =>
        new("TESTX", ExplainFixtures.Peak, ExplainFixtures.GapDown().Max(c => c.Time), refresh);

    [Fact]
    public async Task Explain_GapAfterEarnings_ReturnsCompanySpecificCauseAndNarrative()
    {
        MoveExplanation? explanation = await CreateExplainer().ExplainAsync(Request(), CancellationToken.None);

        explanation.Should().NotBeNull();
        explanation!.Analysis.Scope.Should().Be(MoveScope.CompanySpecific);
        explanation.Causes.Should().ContainSingle().Which.Kind.Should().Be(EvidenceKind.Earnings);
        explanation.Narrative.Should().Be("Provável efeito dos resultados [E1].");
        explanation.NarrativeStatus.Should().Be(NarrativeStatus.Accepted);
        explanation.Summary.Should().Contain("TESTX desceu");
    }

    [Fact]
    public async Task Explain_SameWindowTwice_UsesTheCache()
    {
        MoveExplainer explainer = CreateExplainer();
        await explainer.ExplainAsync(Request(), CancellationToken.None);
        await explainer.ExplainAsync(Request(), CancellationToken.None);

        await _source.Received(1).FetchAsync(Arg.Any<EvidenceQuery>(), Arg.Any<CancellationToken>());

        await explainer.ExplainAsync(Request(true), CancellationToken.None);
        await _source.Received(2).FetchAsync(Arg.Any<EvidenceQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Explain_RejectedNarrative_FallsBackToTemplateAndCountsInStats()
    {
        _narrator.NarrateAsync(Arg.Any<MoveAnalysis>(), Arg.Any<IReadOnlyList<MoveCause>>(), Arg.Any<CancellationToken>())
            .Returns(new NarrationResult(null, NarrativeStatus.Rejected, "modelo local (teste)", ["número que não está nos factos (12%)"]));
        MoveExplainer explainer = CreateExplainer();

        MoveExplanation explanation = (await explainer.ExplainAsync(Request(), CancellationToken.None))!;
        ExplainStats stats = await explainer.GetStatsAsync(CancellationToken.None);

        explanation.Narrative.Should().BeNull();
        explanation.Notes.Should().Contain(n => n.Contains("descartado"));
        explanation.Summary.Should().NotBeEmpty();
        stats.Should().Be(new ExplainStats(1, 0, 1, 0, 0));
    }

    [Fact]
    public async Task Explain_FailingSource_IsReportedButDoesNotBreakTheExplanation()
    {
        _source.FetchAsync(Arg.Any<EvidenceQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<EvidenceItem>>(new HttpRequestException("down")));

        MoveExplanation explanation = (await CreateExplainer().ExplainAsync(Request(), CancellationToken.None))!;

        explanation.Causes.Should().BeEmpty();
        explanation.Notes.Should().Contain("Fonte indisponível: Fake.");
    }

    [Fact]
    public async Task Explain_UnknownSymbol_ReturnsNull() =>
        (await CreateExplainer().ExplainAsync(new MoveRequest("NOPE", null, null), CancellationToken.None)).Should().BeNull();
}
