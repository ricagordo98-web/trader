using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Infrastructure.Currency;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

public class DisplayConversionTests
{
    private static readonly DisplayConversion Euro = new("EUR", new Dictionary<string, double> { ["BTC-USD"] = 0.8, ["AAPL"] = 0.8 });

    [Fact]
    public void Apply_Candles_ScalesPricesButNotVolume()
    {
        IReadOnlyList<Candle> converted = Euro.Apply("BTC-USD", [new Candle(DateTimeOffset.UnixEpoch, 100, 110, 90, 105, 5000)]);

        converted.Should().ContainSingle().Which.Should().Be(new Candle(DateTimeOffset.UnixEpoch, 80, 88, 72, 84, 5000));
    }

    [Fact]
    public void Apply_UnknownSymbol_LeavesCandlesUntouched()
    {
        List<Candle> candles = [new Candle(DateTimeOffset.UnixEpoch, 1, 2, 0.5, 1.5, 10)];

        Euro.Apply("EURUSD=X", candles).Should().BeSameAs(candles);
    }

    [Fact]
    public void Apply_Quote_ScalesPriceChangeSparklineAndRelabelsCurrency()
    {
        InstrumentQuote quote = new("AAPL", "Apple", AssetClass.Stock, "USD", 200, 5, 2.5, [190, 200]);

        InstrumentQuote converted = Euro.Apply(quote);

        converted.Currency.Should().Be("EUR");
        converted.Price.Should().Be(160);
        converted.Change.Should().Be(4);
        converted.ChangePercent.Should().Be(2.5);
        converted.Sparkline.Should().Equal(152, 160);
    }

    [Fact]
    public void Apply_QuoteWithoutPrice_StaysEmpty()
    {
        InstrumentQuote converted = Euro.Apply(new InstrumentQuote("AAPL", "Apple", AssetClass.Stock, "USD", null, null, null, []));

        converted.Price.Should().BeNull();
        converted.Change.Should().BeNull();
    }

    [Fact]
    public void Apply_Quote_NotConverted_KeepsOriginalCurrency()
    {
        InstrumentQuote quote = new("^GSPC", "S&P 500", AssetClass.Index, "USD", 6800, 10, 0.1, [6790, 6800]);

        Euro.Apply(quote).Should().Be(quote);
    }

    [Fact]
    public void Apply_Suggestion_ScalesLevelsAndKeepsFactorToGoBackToNative()
    {
        SuggestionDto suggestion = new(1, "AAPL", "Apple", TradeDirection.Buy, 0.7, 200, 190, 220, 2, "x", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SuggestionStatus.Open, null, null, null, [], []);

        SuggestionDto converted = Euro.Apply(suggestion);

        converted.Entry.Should().Be(160);
        converted.StopLoss.Should().BeApproximately(152, 1e-9);
        converted.TakeProfit.Should().Be(176);
        converted.RiskReward.Should().Be(2);
        (converted.Entry / converted.DisplayFactor).Should().BeApproximately(200, 1e-9);
    }

    [Fact]
    public void Apply_Radar_ScalesEventAndBoardPrices()
    {
        RadarSnapshot snapshot = new(
            DateTimeOffset.UtcNow,
            2,
            [new RadarEvent("AAPL", "Apple", RadarEventKind.Gap, TradeDirection.Buy, 0.5, "gap", 200, DateTimeOffset.UtcNow)],
            [new RadarRow("AAPL", "Apple", AssetClass.Stock, 200, 1, 2, 3, 0.1, DateTimeOffset.UtcNow)]);

        RadarSnapshot converted = Euro.Apply(snapshot);

        converted.Events.Single().Price.Should().Be(160);
        converted.Board.Single().Price.Should().Be(160);
        converted.Board.Single().RelativeVolume.Should().Be(3);
    }

    [Fact]
    public void Drawings_RoundTripThroughDisplayAndStoredCurrency()
    {
        const string stored = """[{"name":"rectangle","points":[{"timestamp":1,"value":100},{"timestamp":2,"value":120}],"extendData":{"label":"x"}}]""";

        string shown = Euro.ToDisplayDrawings("AAPL", stored);

        JsonNode.Parse(shown)![0]!["points"]![0]!["value"]!.GetValue<double>().Should().Be(80);
        JsonNode.Parse(shown)![0]!["points"]![1]!["timestamp"]!.GetValue<long>().Should().Be(2);
        JsonNode.Parse(Euro.ToStoredDrawings("AAPL", shown))![0]!["points"]![1]!["value"]!.GetValue<double>().Should().BeApproximately(120, 1e-9);
    }

    [Fact]
    public void Drawings_SymbolWithoutConversion_ReturnedUnchanged()
    {
        const string stored = """[{"points":[{"timestamp":1,"value":100}]}]""";

        Euro.ToDisplayDrawings("^GSPC", stored).Should().Be(stored);
    }
}

public class DisplayCurrencyServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IMarketDataProvider _provider = Substitute.For<IMarketDataProvider>();
    private TraderDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
        _dbContext.Instruments.RemoveRange(_dbContext.Instruments);
        _dbContext.Instruments.AddRange(
            Instrument("AAPL", AssetClass.Stock, "USD"),
            Instrument("SAP.DE", AssetClass.Stock, "EUR"),
            Instrument("ZC=F", AssetClass.Commodity, "USc"),
            Instrument("EURUSD=X", AssetClass.Forex, "USD"),
            Instrument("^GSPC", AssetClass.Index, "USD"));
        await _dbContext.SaveChangesAsync();
        _provider.GetChartAsync("USDEUR=X", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ChartSeries(null, []));
        _provider.GetChartAsync("EURUSD=X", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ChartSeries(1.25, []));
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static Instrument Instrument(string symbol, AssetClass assetClass, string currency) =>
        new() { Symbol = symbol, Name = symbol, AssetClass = assetClass, Currency = currency, SortOrder = 1 };

    private DisplayCurrencyService CreateService() =>
        new(_dbContext, _provider, new MemoryCache(new MemoryCacheOptions()), NullLogger<DisplayCurrencyService>.Instance);

    [Fact]
    public async Task GetConversion_DefaultsToOriginalCurrency()
    {
        DisplayConversion conversion = await CreateService().GetConversionAsync(CancellationToken.None);

        conversion.Should().Be(DisplayConversion.None);
    }

    [Fact]
    public async Task GetConversion_ToEuro_UsesInverseOfTheAvailablePairAndSkipsForexAndIndices()
    {
        DisplayCurrencyService service = CreateService();
        await service.SetAsync("eur", CancellationToken.None);

        DisplayConversion conversion = await service.GetConversionAsync(CancellationToken.None);

        conversion.Currency.Should().Be("EUR");
        conversion.FactorFor("AAPL").Should().BeApproximately(0.8, 1e-9);
        conversion.FactorFor("SAP.DE").Should().Be(1);
        conversion.FactorFor("ZC=F").Should().BeApproximately(0.008, 1e-9);
        conversion.Applies("EURUSD=X").Should().BeFalse();
        conversion.Applies("^GSPC").Should().BeFalse();
    }

    [Fact]
    public async Task GetConversion_WhenRateUnavailable_OmitsOnlyThoseInstruments()
    {
        _provider.GetChartAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<ChartSeries>(new HttpRequestException("down")));
        DisplayCurrencyService service = CreateService();
        await service.SetAsync("EUR", CancellationToken.None);

        DisplayConversion conversion = await service.GetConversionAsync(CancellationToken.None);

        conversion.Applies("AAPL").Should().BeFalse();
        conversion.Applies("ZC=F").Should().BeFalse();
        conversion.Applies("SAP.DE").Should().BeTrue();
    }

    [Fact]
    public async Task Set_UnsupportedCurrency_IsRejected()
    {
        DisplayCurrencyService service = CreateService();

        (await service.SetAsync("XYZ", CancellationToken.None)).Should().NotBeNull();
        (await service.GetAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Set_EmptyValue_RestoresOriginalCurrency()
    {
        DisplayCurrencyService service = CreateService();
        await service.SetAsync("EUR", CancellationToken.None);

        (await service.SetAsync(string.Empty, CancellationToken.None)).Should().BeNull();

        (await service.GetConversionAsync(CancellationToken.None)).Should().Be(DisplayConversion.None);
    }
}
