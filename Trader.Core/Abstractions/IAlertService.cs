using Trader.Core.Alerts;

namespace Trader.Core.Abstractions;

public interface IAlertService
{
    Task RaiseAsync(IReadOnlyList<Alert> alerts, CancellationToken cancellationToken);

    Task<AlertFeed> GetFeedAsync(string userId, int limit, CancellationToken cancellationToken);

    Task MarkSeenAsync(string userId, CancellationToken cancellationToken);

    Task<NtfySettings> GetNtfyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SaveNtfyAsync(NtfySettings settings, CancellationToken cancellationToken);

    Task<string?> SendTestAsync(CancellationToken cancellationToken);
}

public interface IAlertNotifier
{
    Task NotifyAlertsAsync(IReadOnlyList<AlertDto> alerts, CancellationToken cancellationToken);
}

public interface IAlertPublisher
{
    Task PublishAsync(NtfySettings settings, IReadOnlyList<AlertDto> alerts, CancellationToken cancellationToken);
}
