using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Trader.Core.Journal;
using Trader.Core.Models;

namespace Trader.Tests;

public class TradeCsvTests
{
    private const string Trading212 = """
        Action,Time,ISIN,Ticker,Name,No. of shares,Price / share,Currency (Price / share),Exchange rate,Result,Currency (Result),Total,Currency (Total)
        Deposit,2026-01-02 09:00:00,,,,,,,,,,1000.00,EUR
        Market buy,2026-01-05 15:31:10,US0378331005,AAPL,Apple,10,150.50,USD,1.10,,,1368.18,EUR
        Market buy,2026-01-06 16:00:00,US0378331005,AAPL,Apple,5,152.00,USD,1.10,,,690.9,EUR
        Market sell,2026-01-10 14:00:00,US0378331005,AAPL,Apple,12,160.00,USD,1.10,,,1745.45,EUR
        Dividend (Ordinary),2026-01-11 10:00:00,US0378331005,AAPL,Apple,15,0.24,USD,1.10,,,3.27,EUR
        """;

    private const string XtbLike = """
        Position;Symbol;Type;Volume;Open time;Open price;Close time;Close price;SL;TP;Commission
        123;EURUSD;BUY;0,5;05.01.2026 10:15:00;1,10250;07.01.2026 18:00:00;1,10980;1,0950;1,1150;1,50
        124;GOLD;SELL;1;06.01.2026 09:00:00;2.050,50;;;;;
        """;

    [Fact]
    public void Parse_Trading212_PairsBuysAndSellsFifo()
    {
        ImportPreview preview = TradeCsv.Parse(Trading212);

        preview.Format.Should().Contain("Transações");
        preview.Trades.Should().HaveCount(3);

        ImportedTrade first = preview.Trades[0];
        first.Symbol.Should().Be("AAPL");
        first.Quantity.Should().Be(10);
        first.EntryPrice.Should().Be(150.5);
        first.ExitPrice.Should().Be(160);
        first.FxRate.Should().BeApproximately(1 / 1.10, 1e-9);

        ImportedTrade second = preview.Trades[1];
        second.Quantity.Should().Be(2);
        second.EntryPrice.Should().Be(152);
        second.ExitPrice.Should().Be(160);

        ImportedTrade open = preview.Trades[2];
        open.Quantity.Should().Be(3);
        open.ExitPrice.Should().BeNull();
    }

    [Fact]
    public void Parse_PositionsWithSemicolonsAndDecimalCommas_ReadsClosedAndOpenTrades()
    {
        ImportPreview preview = TradeCsv.Parse(XtbLike);

        preview.Format.Should().Contain("Posições");
        preview.Trades.Should().HaveCount(2);

        ImportedTrade closed = preview.Trades[0];
        closed.Symbol.Should().Be("EURUSD");
        closed.Direction.Should().Be(TradeDirection.Buy);
        closed.Quantity.Should().Be(0.5);
        closed.EntryPrice.Should().Be(1.1025);
        closed.ExitPrice.Should().Be(1.1098);
        closed.StopLoss.Should().Be(1.095);
        closed.TakeProfit.Should().Be(1.115);
        closed.Fees.Should().Be(1.5);

        ImportedTrade open = preview.Trades[1];
        open.Direction.Should().Be(TradeDirection.Sell);
        open.EntryPrice.Should().Be(2050.5);
        open.ExitPrice.Should().BeNull();
    }

    [Fact]
    public void Parse_SellWithoutBuy_WarnsAndSkips()
    {
        ImportPreview preview = TradeCsv.Parse("Action,Time,Ticker,No. of shares,Price / share\nMarket sell,2026-01-10 14:00:00,TSLA,3,200\n");

        preview.Trades.Should().BeEmpty();
        preview.Warnings.Should().ContainSingle().Which.Should().Contain("TSLA");
    }

    [Fact]
    public void Parse_UnknownColumns_ReturnsHelpfulWarning()
    {
        ImportPreview preview = TradeCsv.Parse("foo,bar\n1,2\n");

        preview.Trades.Should().BeEmpty();
        preview.Warnings.Should().ContainSingle().Which.Should().Contain("colunas");
    }

    [Fact]
    public void Parse_EmptyFile_ReturnsWarning() =>
        TradeCsv.Parse(string.Empty).Warnings.Should().NotBeEmpty();

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("1 000", 1000)]
    public void TryNumber_HandlesLocaleFormats(string text, double expected)
    {
        TradeCsv.TryNumber(text, out double value).Should().BeTrue();
        value.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Export_ThenParse_RoundTripsTrades()
    {
        JournalTradeDto trade = new(
            1, "AAPL", "Apple, Inc.", TradeDirection.Buy, new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero), 150.5, 10, 148, 160, 0.9, 1.25,
            "XTB", "nota \"com\" aspas, e vírgula", null, TradeStatus.Closed, new DateTimeOffset(2026, 1, 8, 10, 0, 0, TimeSpan.Zero), 158, 100, 75, 1.5, null, null);
        JournalTradeDto open = trade with { Id = 2, Status = TradeStatus.Open, ExitTime = null, ExitPrice = null, EntryPrice = 151 };

        string csv = TradeCsv.Export([trade, open]);
        ImportPreview preview = TradeCsv.Parse(csv);

        preview.Trades.Should().HaveCount(2);
        preview.Trades[0].Should().Be(new ImportedTrade("AAPL", TradeDirection.Buy, trade.EntryTime, 150.5, 10, 148, 160, 0.9, 1.25, "XTB", trade.ExitTime, 158));
        preview.Trades[1].ExitPrice.Should().BeNull();
        csv.Should().Contain("\"Apple, Inc.\"").And.Contain("\"nota \"\"com\"\" aspas, e vírgula\"");
    }
}

public class TradeCsvIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private const string Sample = "Symbol,Type,Open time,Open price,Quantity,Close time,Close price\nAAPL,buy,2026-01-05 10:00:00,150,10,2026-01-07 10:00:00,155\nMSFT,sell,2026-01-06 10:00:00,400,2,,\n";

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(TraderFactory factory, string email, HttpClient? creator = null)
    {
        HttpClient client = factory.CreateClient();
        (await (creator ?? client).PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static StringContent Csv(string text) => new(text, Encoding.UTF8, "text/plain");

    [Fact]
    public async Task Import_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().PostAsync("/api/journal/import/preview", Csv(Sample))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateClient().GetAsync("/api/journal/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Import_CreatesTradesSkipsDuplicatesAndExportsThem()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "a@example.com");

        ImportPreview preview = (await (await client.PostAsync("/api/journal/import/preview", Csv(Sample))).Content.ReadFromJsonAsync<ImportPreview>())!;
        preview.Trades.Should().HaveCount(2);

        ImportSummary first = (await (await client.PostAsync("/api/journal/import", Csv(Sample))).Content.ReadFromJsonAsync<ImportSummary>())!;
        first.Imported.Should().Be(2);
        first.Skipped.Should().Be(0);
        first.Errors.Should().BeEmpty();

        ImportSummary second = (await (await client.PostAsync("/api/journal/import", Csv(Sample))).Content.ReadFromJsonAsync<ImportSummary>())!;
        second.Imported.Should().Be(0);
        second.Skipped.Should().Be(2);

        List<JournalTradeDto> trades = (await client.GetFromJsonAsync<List<JournalTradeDto>>("/api/journal/trades"))!;
        trades.Should().HaveCount(2);
        trades.Single(t => t.Symbol == "AAPL").Status.Should().Be(TradeStatus.Closed);
        trades.Single(t => t.Symbol == "MSFT").Status.Should().Be(TradeStatus.Open);

        string exported = await client.GetStringAsync("/api/journal/export");
        exported.Should().Contain("AAPL").And.Contain("MSFT");
    }

    [Fact]
    public async Task Import_PartiallyClosedLot_ImportsBothPartsAndIsIdempotent()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "a@example.com");
        const string csv = """
            Action,Time,Ticker,No. of shares,Price / share
            Market buy,2026-01-05 10:00:00,AAPL,5,150
            Market sell,2026-01-06 10:00:00,AAPL,2,160
            """;

        ImportSummary first = (await (await client.PostAsync("/api/journal/import", Csv(csv))).Content.ReadFromJsonAsync<ImportSummary>())!;
        ImportSummary second = (await (await client.PostAsync("/api/journal/import", Csv(csv))).Content.ReadFromJsonAsync<ImportSummary>())!;

        first.Imported.Should().Be(2);
        second.Imported.Should().Be(0);
        second.Skipped.Should().Be(2);
    }

    [Fact]
    public async Task Import_UnrecognizedFile_ReturnsBadRequest()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "a@example.com");

        (await client.PostAsync("/api/journal/import", Csv("foo,bar\n1,2\n"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_IsIsolatedPerUser()
    {
        await using TraderFactory factory = new();
        HttpClient first = await SignIn(factory, "a@example.com");
        HttpClient second = await SignIn(factory, "b@example.com", first);

        await first.PostAsync("/api/journal/import", Csv(Sample));

        (await second.GetFromJsonAsync<List<JournalTradeDto>>("/api/journal/trades"))!.Should().BeEmpty();
        ImportSummary summary = (await (await second.PostAsync("/api/journal/import", Csv(Sample))).Content.ReadFromJsonAsync<ImportSummary>())!;
        summary.Imported.Should().Be(2);
    }
}
