using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Backtesting;
using Trader.Core.Journal;
using Trader.Core.Models;
using Trader.Infrastructure.MarketData;
using Trader.Infrastructure.News;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

public class InstrumentCatalogTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private TraderDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void Catalog_SymbolsAreUniqueAndWellFormed()
    {
        List<string> symbols = InstrumentCatalog.AllItems().Select(i => i.Symbol).ToList();

        symbols.Should().OnlyHaveUniqueItems();
        symbols.All(s => s.Length is >= 1 and <= 20 && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Z0-9.=\-^]+$")).Should().BeTrue();
        InstrumentCatalog.AllItems().All(i => i.Name.Length > 0 && i.Currency.Length is >= 3 and <= 8).Should().BeTrue();
    }

    [Fact]
    public void Catalog_CoversEveryAssetClass() =>
        InstrumentCatalog.AllItems().Select(i => i.Class).Distinct().Should().BeEquivalentTo(Enum.GetValues<AssetClass>());

    [Fact]
    public async Task Apply_OnSeededDatabase_AddsNewInstrumentsAndKeepsExistingOnes()
    {
        int before = await _dbContext.Instruments.CountAsync();

        int added = await InstrumentCatalog.ApplyAsync(_dbContext, NullLogger.Instance, CancellationToken.None);

        added.Should().BeGreaterThan(100);
        (await _dbContext.Instruments.CountAsync()).Should().Be(before + added);
        (await _dbContext.Instruments.AnyAsync(i => i.Symbol == "AAPL")).Should().BeTrue();
        (await _dbContext.Instruments.Select(i => i.SortOrder).Distinct().CountAsync()).Should().Be(before + added);
        (await _dbContext.Instruments.CountAsync(i => i.AssetClass == AssetClass.Crypto)).Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task Apply_RunsOnlyOncePerVersion_SoRemovedInstrumentsStayRemoved()
    {
        await InstrumentCatalog.ApplyAsync(_dbContext, NullLogger.Instance, CancellationToken.None);
        await _dbContext.Instruments.Where(i => i.Symbol == "BTC-USD").ExecuteDeleteAsync();

        int added = await InstrumentCatalog.ApplyAsync(_dbContext, NullLogger.Instance, CancellationToken.None);

        added.Should().Be(0);
        (await _dbContext.Instruments.AnyAsync(i => i.Symbol == "BTC-USD")).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_WhenUserAlreadyAddedASymbol_DoesNotDuplicateIt()
    {
        _dbContext.Instruments.Add(new Instrument { Symbol = "BTC-USD", Name = "Meu Bitcoin", AssetClass = AssetClass.Crypto, Currency = "USD", SortOrder = 500 });
        await _dbContext.SaveChangesAsync();

        await InstrumentCatalog.ApplyAsync(_dbContext, NullLogger.Instance, CancellationToken.None);

        (await _dbContext.Instruments.CountAsync(i => i.Symbol == "BTC-USD")).Should().Be(1);
        (await _dbContext.Instruments.SingleAsync(i => i.Symbol == "BTC-USD")).Name.Should().Be("Meu Bitcoin");
    }
}

public class AssetClassSupportTests
{
    [Theory]
    [InlineData(AssetClass.Forex, "EURUSD=X", 1.1, 5)]
    [InlineData(AssetClass.Forex, "USDJPY=X", 150, 3)]
    [InlineData(AssetClass.Stock, "AAPL", 340.5, 2)]
    [InlineData(AssetClass.Stock, "PENNY", 0.42, 4)]
    [InlineData(AssetClass.Crypto, "BTC-USD", 82000, 2)]
    [InlineData(AssetClass.Crypto, "XRP-USD", 2.3, 4)]
    [InlineData(AssetClass.Crypto, "DOGE-USD", 0.18, 5)]
    [InlineData(AssetClass.Crypto, "SHIB-USD", 0.000013, 9)]
    [InlineData(AssetClass.Crypto, "PEPE-USD", 0.0021, 7)]
    [InlineData(AssetClass.Index, "^GSPC", 6800, 2)]
    public void Decimals_DependsOnClassAndPriceMagnitude(AssetClass assetClass, string symbol, double price, int expected) =>
        PriceFormat.Decimals(assetClass, symbol, price).Should().Be(expected);

    [Fact]
    public void Decimals_UnknownPrice_FallsBackToTwo() =>
        PriceFormat.Decimals(AssetClass.Crypto, "BTC-USD", null).Should().Be(2);

    [Fact]
    public void CostModel_UsesDedicatedCostsForCryptoAndIndices()
    {
        CostModel costs = new();

        costs.RoundTripPercent(AssetClass.Crypto).Should().Be(costs.CryptoRoundTripPercent);
        costs.RoundTripPercent(AssetClass.Index).Should().Be(costs.IndexRoundTripPercent);
        costs.CryptoRoundTripPercent.Should().BeGreaterThan(costs.StockRoundTripPercent);
    }

    [Fact]
    public void CostModelValidation_RejectsOutOfRangeCryptoCost()
    {
        BacktestRequest request = new() { Costs = new CostModel { CryptoRoundTripPercent = 9 } };

        BacktestRequestValidator.Validate(request).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(AssetClass.Crypto, 2)]
    [InlineData(AssetClass.Index, 20)]
    [InlineData(AssetClass.Forex, 30)]
    public void DefaultLeverage_FollowsRetailLimits(AssetClass assetClass, double expected) =>
        PositionCalculator.DefaultLeverage(assetClass).Should().Be(expected);

    [Theory]
    [InlineData("BTC-USD", "BINANCE:BTCUSDT")]
    [InlineData("SOL-USD", "BINANCE:SOLUSDT")]
    [InlineData("^GSPC", null)]
    [InlineData("EDP.LS", null)]
    public void FinnhubMapper_HandlesNewSymbolFamilies(string symbol, string? expected) =>
        FinnhubSymbolMapper.ToFinnhub(symbol).Should().Be(expected);

    private readonly TickerExtractor _extractor = new();

    [Theory]
    [InlineData("Bitcoin jumps above 90,000 as ETF inflows surge", "BTC-USD")]
    [InlineData("Solana network activity hits a record", "SOL-USD")]
    [InlineData("Berkshire Hathaway boosts its cash pile", "BRK-B")]
    [InlineData("Coca-Cola beats estimates on pricing", "KO")]
    [InlineData("Visa Inc reports payment volume growth", "V")]
    [InlineData("Platinum rallies on supply worries", "PL=F")]
    [InlineData("The Nikkei closes at a record high", "^N225")]
    [InlineData("EDP Renováveis signs a new wind deal", "EDP.LS")]
    [InlineData("Traders watch $MA and $V after earnings", "MA")]
    public void Extract_FindsTheNewCatalogEntities(string text, string expected) =>
        _extractor.Extract(text).Should().Contain(expected);

    [Fact]
    public void Extract_ShortTickersAreNotMatchedWithoutCashtag() =>
        _extractor.Extract("V for victory, MA is in the building and KO in boxing").Should().NotContain(["V", "MA", "KO"]);
}

public class SuggestionAlertGroupingTests
{
    private static Suggestion Make(string symbol, double confidence) => new()
    {
        Id = 1,
        Symbol = symbol,
        Direction = TradeDirection.Buy,
        Confidence = confidence,
        Entry = 100,
        StopLoss = 98,
        TakeProfit = 105
    };

    [Fact]
    public void ForNewSuggestions_FewSuggestions_AreAlertedIndividually()
    {
        IReadOnlyList<Alert> alerts = AlertFactory.ForNewSuggestions([Make("AAA", 0.7), Make("BBB", 0.65)], DateTimeOffset.UtcNow);

        alerts.Should().HaveCount(2);
        alerts.Select(a => a.Title).Should().OnlyContain(t => t.StartsWith("Nova sugestão"));
    }

    [Fact]
    public void ForNewSuggestions_ManySuggestions_AreCoalescedIntoOneAlert()
    {
        List<Suggestion> many = Enumerable.Range(0, 12).Select(i => Make($"S{i}", 0.6 + i / 100.0)).ToList();

        IReadOnlyList<Alert> alerts = AlertFactory.ForNewSuggestions(many, DateTimeOffset.UtcNow);

        Alert alert = alerts.Should().ContainSingle().Subject;
        alert.Title.Should().Be("12 novas sugestões");
        alert.Message.Should().Contain("S11").And.Contain("S10").And.Contain("S9");
    }
}
