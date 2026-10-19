using FluentAssertions;
using Trader.Infrastructure.News;
using Trader.Infrastructure.News.Sources;

namespace Trader.Tests;

public class TickerExtractorTests
{
    private readonly TickerExtractor _extractor = new();

    [Theory]
    [InlineData("Apple unveils new iPhone as AAPL rallies", "AAPL")]
    [InlineData("Traders pile into $nvda ahead of earnings", "NVDA")]
    [InlineData("Gold prices hit a record high", "GC=F")]
    [InlineData("EUR/USD slips after ECB comments", "EURUSD=X")]
    [InlineData("Alphabet beats estimates", "GOOGL")]
    public void Extract_KnownEntity_ReturnsSymbol(string text, string expected)
    {
        _extractor.Extract(text).Should().Contain(expected);
    }

    [Theory]
    [InlineData("I ate an apple for lunch")]
    [InlineData("Goldman Sachs raises its outlook")]
    [InlineData("Stocks were mixed on Tuesday")]
    public void Extract_NoEntity_ReturnsEmpty(string text)
    {
        _extractor.Extract(text).Should().BeEmpty();
    }

    [Fact]
    public void Extract_RepeatedMentions_ReturnsDistinctSymbols()
    {
        _extractor.Extract("Tesla and TSLA and $TSLA").Should().Equal("TSLA");
    }
}

public class ArticleHasherTests
{
    [Fact]
    public void HashUrl_TrackingParametersAndTrailingSlash_AreIgnored()
    {
        string a = ArticleHasher.HashUrl("https://Example.com/news/story/?utm_source=x&id=7");
        string b = ArticleHasher.HashUrl("https://example.com/news/story?id=7&utm_campaign=y");

        a.Should().Be(b);
    }

    [Fact]
    public void HashUrl_DifferentIds_DiffersByHash()
    {
        ArticleHasher.HashUrl("https://example.com/a?id=1").Should().NotBe(ArticleHasher.HashUrl("https://example.com/a?id=2"));
    }

    [Fact]
    public void HashTitle_PunctuationAndCaseDifferences_AreIgnored()
    {
        ArticleHasher.HashTitle("Fed holds rates steady!").Should().Be(ArticleHasher.HashTitle("FED HOLDS RATES, STEADY"));
    }
}

public class ConnectorParsingTests
{
    [Fact]
    public void GdeltParse_ValidPayload_ReturnsArticles()
    {
        const string json = """
            {"articles":[{"url":"https://a.com/x","title":"Stocks rally","seendate":"20261019T110000Z","domain":"a.com"}]}
            """;

        var articles = GdeltConnector.Parse(json);

        articles.Should().ContainSingle();
        articles[0].Source.Should().Be("a.com");
        articles[0].PublishedAt.Should().Be(new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StockTwitsParse_MessageWithSymbols_KeepsSymbols()
    {
        const string json = """
            {"messages":[{"id":5,"body":"$AAPL up 2%","created_at":"2026-10-19T10:00:00Z","user":{"username":"bob"},"symbols":[{"symbol":"AAPL"}]}]}
            """;

        var articles = StockTwitsConnector.Parse(json);

        articles.Should().ContainSingle();
        articles[0].Url.Should().Be("https://stocktwits.com/bob/message/5");
        articles[0].Symbols.Should().Equal("AAPL");
    }

    [Fact]
    public void FinnhubParse_RelatedSymbols_AreSplit()
    {
        const string json = """
            [{"headline":"Deal","summary":"s","url":"https://f.com/1","datetime":1760000000,"source":"Reuters","related":"AAPL,MSFT"}]
            """;

        FinnhubConnector.Parse(json)[0].Symbols.Should().Equal("AAPL", "MSFT");
    }

    [Fact]
    public void TextCleaner_HtmlAndLongText_IsStrippedAndTruncated()
    {
        TextCleaner.Clean("<p>Hello &amp; <b>world</b></p>", 100).Should().Be("Hello & world");
        TextCleaner.Clean(new string('a', 50), 10).Should().HaveLength(10);
    }
}

public class FinBertSentimentAnalyzerTests
{
    private static string? FindModelDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Trader.Web", "models", "finbert");
            if (File.Exists(Path.Combine(candidate, "model.onnx")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    [Fact]
    public void Analyze_PositiveAndNegativeHeadlines_ReturnsExpectedSign()
    {
        string? modelDirectory = FindModelDirectory();
        if (modelDirectory is null)
        {
            return;
        }

        using FinBertSentimentAnalyzer analyzer = new(modelDirectory);

        analyzer.Analyze("Company reports record profits and raises full-year guidance").Should().BeGreaterThan(0.5);
        analyzer.Analyze("Company misses estimates, cuts outlook and announces layoffs").Should().BeLessThan(-0.5);
    }
}

public class SummaryRedundancyTests
{
    [Theory]
    [InlineData("Apple Stock Holds Strong - Investor's Business Daily", "Apple Stock Holds Strong Investor's Business Daily", true)]
    [InlineData("Fed holds rates", "", true)]
    [InlineData("Fed holds rates", "Officials signalled no cuts before spring.", false)]
    public void IsRedundantSummary_Cases_ReturnsExpected(string title, string summary, bool expected)
    {
        ArticleHasher.IsRedundantSummary(title, summary).Should().Be(expected);
    }
}
