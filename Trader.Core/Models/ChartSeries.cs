namespace Trader.Core.Models;

public sealed record ChartSeries(
    double? LastPrice,
    IReadOnlyList<Candle> Candles);
