namespace Trader.Core.Models;

public sealed class MoveExplanationRecord
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string NarrativeStatus { get; set; } = string.Empty;
    public string Json { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
