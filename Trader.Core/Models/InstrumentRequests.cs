namespace Trader.Core.Models;

public sealed record WatchlistItem(string Symbol, string Name, AssetClass AssetClass, string Currency);

public sealed record AddInstrumentRequest(string Symbol, string Name, AssetClass AssetClass, string Currency);

public sealed record AddInstrumentResult(Instrument? Instrument, string? Error);

public sealed record SourceStatus(
    string Name,
    bool Enabled,
    string? DisabledReason,
    int IntervalMinutes,
    DateTimeOffset? LastRun,
    int LastFetched,
    int LastAdded,
    string? LastError);
