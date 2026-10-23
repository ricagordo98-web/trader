using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Alerts;

public sealed class AlertService(TraderDbContext dbContext, IAlertNotifier notifier, IAlertPublisher publisher, IUserPreferences preferences, ILogger<AlertService> logger) : IAlertService
{
    public async Task RaiseAsync(IReadOnlyList<Alert> alerts, CancellationToken cancellationToken)
    {
        if (alerts.Count == 0)
        {
            return;
        }

        dbContext.Alerts.AddRange(alerts);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (IGrouping<string?, Alert> group in alerts.GroupBy(a => a.UserId))
        {
            await notifier.NotifyAlertsAsync(group.Select(a => ToDto(a, true)).ToList(), group.Key, cancellationToken);
        }

        NtfySettings ntfy = await GetNtfyAsync(cancellationToken);
        int hour = DateTime.Now.Hour;

        foreach (IGrouping<string?, Alert> group in alerts.GroupBy(a => a.UserId))
        {
            UserPreferences? own = group.Key is null ? null : await preferences.GetAsync(group.Key, cancellationToken);
            NtfySettings target = ntfy with { Topic = NtfyRouting.ResolveTopic(ntfy, own) };
            List<AlertDto> dtos = group
                .Select(a => (Alert: a, Dto: ToDto(a, true)))
                .Where(x => NtfyRouting.ShouldPush(x.Dto, x.Alert.Quiet, own, hour))
                .Select(x => x.Dto)
                .ToList();

            if (!target.IsConfigured || dtos.Count == 0)
            {
                continue;
            }

            try
            {
                await publisher.PublishAsync(target, dtos, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Could not publish alerts to ntfy: {Message}", ex.Message);
            }
        }
    }

    public async Task<NtfySettings> GetNtfyAsync(CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == NtfyKey, cancellationToken);

        return setting is null
            ? new NtfySettings(NtfySettings.DefaultServer, string.Empty)
            : JsonSerializer.Deserialize<NtfySettings>(setting.Value) ?? new NtfySettings(NtfySettings.DefaultServer, string.Empty);
    }

    public async Task<IReadOnlyList<string>> SaveNtfyAsync(NtfySettings settings, CancellationToken cancellationToken)
    {
        NtfySettings clean = new(settings.Server.Trim().TrimEnd('/'), settings.Topic.Trim(), settings.PublicUrl.Trim().TrimEnd('/'));
        IReadOnlyList<string> errors = NtfySettingsValidator.Validate(clean);

        if (errors.Count > 0)
        {
            return errors;
        }

        string json = JsonSerializer.Serialize(clean);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == NtfyKey, cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = NtfyKey, Value = json });
        }
        else
        {
            setting.Value = json;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return [];
    }

    public async Task<string?> SendTestAsync(CancellationToken cancellationToken)
    {
        NtfySettings ntfy = await GetNtfyAsync(cancellationToken);

        if (!ntfy.IsConfigured)
        {
            return "Define primeiro o tópico.";
        }

        AlertDto test = new(0, AlertKind.NewSuggestion, "TRADER", "Teste de notificação", "Se estás a ler isto, as notificações estão a funcionar.", 0, DateTimeOffset.UtcNow, true);

        try
        {
            await publisher.PublishAsync(ntfy, [test], cancellationToken);
            return null;
        }
        catch (HttpRequestException ex)
        {
            return $"Não foi possível contactar o servidor ntfy: {ex.Message}";
        }
    }

    public async Task<AlertFeed> GetFeedAsync(string userId, int limit, CancellationToken cancellationToken)
    {
        DateTimeOffset seenAt = await GetSeenAtAsync(userId, cancellationToken);

        List<Alert> rows = await dbContext.Alerts
            .AsNoTracking()
            .Where(a => a.UserId == null || a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

        int unread = await dbContext.Alerts.CountAsync(a => (a.UserId == null || a.UserId == userId) && a.CreatedAt > seenAt, cancellationToken);

        return new AlertFeed(rows.Select(a => ToDto(a, a.CreatedAt > seenAt)).ToList(), unread);
    }

    public async Task MarkSeenAsync(string userId, CancellationToken cancellationToken)
    {
        string value = DateTimeOffset.UtcNow.UtcTicks.ToString(CultureInfo.InvariantCulture);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == SeenKey(userId), cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = SeenKey(userId), Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<DateTimeOffset> GetSeenAtAsync(string userId, CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SeenKey(userId), cancellationToken);

        return setting is not null && long.TryParse(setting.Value, CultureInfo.InvariantCulture, out long ticks)
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : DateTimeOffset.MinValue;
    }

    public async Task<IReadOnlyList<PriceAlertDto>> ListPriceAlertsAsync(string userId, CancellationToken cancellationToken)
    {
        List<PriceAlert> rows = await dbContext.PriceAlerts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.TriggeredAt != null)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<(PriceAlertDto? Alert, IReadOnlyList<string> Errors)> AddPriceAlertAsync(string userId, CreatePriceAlertRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = PriceAlertValidator.Validate(request);

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        int active = await dbContext.PriceAlerts.CountAsync(a => a.UserId == userId && a.TriggeredAt == null, cancellationToken);

        if (active >= MaxActivePriceAlerts)
        {
            return (null, [$"Já tens {MaxActivePriceAlerts} alertas ativos. Apaga algum antes de criar outro."]);
        }

        PriceAlert alert = new()
        {
            UserId = userId,
            Symbol = request.Symbol.Trim().ToUpperInvariant(),
            Condition = request.Condition,
            Price = request.Price,
            Note = request.Note?.Trim() ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.PriceAlerts.Add(alert);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (ToDto(alert), []);
    }

    public async Task<bool> DeletePriceAlertAsync(string userId, long id, CancellationToken cancellationToken)
    {
        PriceAlert? alert = await dbContext.PriceAlerts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken);

        if (alert is null)
        {
            return false;
        }

        dbContext.PriceAlerts.Remove(alert);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static PriceAlertDto ToDto(PriceAlert alert) =>
        new(alert.Id, alert.Symbol, alert.Condition, alert.Price, alert.Note, alert.CreatedAt, alert.TriggeredAt);

    private const int MaxActivePriceAlerts = 50;

    private const string NtfyKey = "alerts.ntfy";

    private static string SeenKey(string userId) => $"alerts.seen:{userId}";

    private static AlertDto ToDto(Alert alert, bool unread) =>
        new(alert.Id, alert.Kind, alert.Symbol, alert.Title, alert.Message, alert.SuggestionId, alert.CreatedAt, unread, alert.TradeId);
}
