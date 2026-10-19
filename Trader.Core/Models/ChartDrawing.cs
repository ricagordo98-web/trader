namespace Trader.Core.Models;

public sealed class ChartDrawing
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Json { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}
