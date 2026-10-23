namespace Trader.Shared.Components;

public sealed record ChartMarker(long Timestamp, string Label, string Color, string Key = "");
