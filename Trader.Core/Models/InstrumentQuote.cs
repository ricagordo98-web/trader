namespace Trader.Core.Models;

public sealed record InstrumentQuote(
    string Symbol,
    string Name,
    AssetClass AssetClass,
    string Currency,
    double? Price,
    double? Change,
    double? ChangePercent,
    IReadOnlyList<double> Sparkline,
    double DisplayFactor = 1,
    DateTimeOffset? AsOf = null);
