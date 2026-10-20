namespace Trader.Core.Abstractions;

public sealed record LiveTrade(DateTimeOffset Time, double Price, double Volume);

public interface ILiveTradeFeed
{
    bool Track(string symbol);

    IReadOnlyList<LiveTrade> GetTrades(string symbol, DateTimeOffset since);
}
