namespace Trader.Core.Models;

public sealed class Instrument
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetClass AssetClass { get; set; }
    public string Currency { get; set; } = "USD";
    public int SortOrder { get; set; }
}
