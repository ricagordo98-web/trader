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
    Radar
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

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record AlertDto(long Id, AlertKind Kind, string Symbol, string Title, string Message, long SuggestionId, DateTimeOffset CreatedAt, bool Unread);

public sealed record AlertFeed(IReadOnlyList<AlertDto> Items, int UnreadCount);

public sealed record NtfySettings(string Server, string Topic)
{
    public const string DefaultServer = "https://ntfy.sh";

    public bool IsConfigured => Topic.Length > 0;
}
