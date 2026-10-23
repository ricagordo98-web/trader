namespace Trader.Core.Alerts;

public enum AlertKind
{
    NewSuggestion,
    TakeProfit,
    StopLoss,
    Expired,
    PriceTarget,
    TradeStopLoss,
    TradeTakeProfit,
    Calendar,
    DailySummary,
    Radar,
    Halt,
    Rule
}

public sealed class Alert
{
    public long Id { get; set; }

    public string? UserId { get; set; }

    public AlertKind Kind { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public long SuggestionId { get; set; }

    public long TradeId { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Quiet { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record AlertDto(long Id, AlertKind Kind, string Symbol, string Title, string Message, long SuggestionId, DateTimeOffset CreatedAt, bool Unread, long TradeId = 0);

public sealed record AlertFeed(IReadOnlyList<AlertDto> Items, int UnreadCount);

public sealed record NtfySettings(string Server, string Topic, string PublicUrl = "")
{
    public const string DefaultServer = "https://ntfy.sh";

    public bool IsConfigured => Topic.Length > 0;
}
