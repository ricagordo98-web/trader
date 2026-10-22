using FluentAssertions;
using Trader.Core.Alerts;
using Trader.Core.Journal;
using Trader.Core.Models;

namespace Trader.Tests;

public class AlertLinksTests
{
    private static AlertDto Alert(AlertKind kind, string symbol = "AAPL", long suggestionId = 0, long tradeId = 0) =>
        new(1, kind, symbol, "t", "m", suggestionId, DateTimeOffset.UtcNow, true, tradeId);

    [Theory]
    [InlineData(AlertKind.NewSuggestion, 42, "/prediction?suggestion=42")]
    [InlineData(AlertKind.TakeProfit, 7, "/prediction?suggestion=7")]
    [InlineData(AlertKind.StopLoss, 7, "/prediction?suggestion=7")]
    [InlineData(AlertKind.Expired, 7, "/prediction?suggestion=7")]
    [InlineData(AlertKind.NewSuggestion, 0, "/prediction")]
    public void For_SuggestionAlerts_GoToTheSuggestion(AlertKind kind, long suggestionId, string expected) =>
        AlertLinks.For(Alert(kind, suggestionId: suggestionId)).Should().Be(expected);

    [Theory]
    [InlineData(AlertKind.TradeStopLoss)]
    [InlineData(AlertKind.TradeTakeProfit)]
    public void For_TradeAlerts_GoToTheTradeNotTheSuggestion(AlertKind kind) =>
        AlertLinks.For(Alert(kind, suggestionId: 5, tradeId: 12)).Should().Be("/journal?trade=12");

    [Fact]
    public void For_TradeAlertWithoutTradeId_GoesToTheJournal() =>
        AlertLinks.For(Alert(AlertKind.TradeStopLoss)).Should().Be("/journal");

    [Theory]
    [InlineData(AlertKind.PriceTarget, "BTC-USD", "/trading?symbol=BTC-USD")]
    [InlineData(AlertKind.Radar, "EDP.LS", "/trading?symbol=EDP.LS")]
    [InlineData(AlertKind.PriceTarget, "GC=F", "/trading?symbol=GC%3DF")]
    [InlineData(AlertKind.Radar, "RADAR", "/prediction?tab=radar")]
    [InlineData(AlertKind.Calendar, "MACRO", "/calendar")]
    [InlineData(AlertKind.DailySummary, "", "/")]
    public void For_OtherAlerts_GoToTheRightPlace(AlertKind kind, string symbol, string expected) =>
        AlertLinks.For(Alert(kind, symbol)).Should().Be(expected);

    [Fact]
    public void ForTradeLevel_CarriesTheTradeId()
    {
        JournalTrade trade = new() { Id = 99, UserId = "u", Symbol = "AAPL", EntryPrice = 100, StopLoss = 95 };

        AlertFactory.ForTradeLevel(trade, TradeLevel.StopLoss, 94, DateTimeOffset.UtcNow).TradeId.Should().Be(99);
    }
}
