namespace Trader.Core.Models;

public sealed record ChartSeries(
    double? LastPrice,
    IReadOnlyList<Candle> Candles,
    DateTimeOffset? LastPriceTime = null);

public sealed record LiveTick(string Symbol, double Price, DateTimeOffset Time);
