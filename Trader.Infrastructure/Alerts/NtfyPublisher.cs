using System.Net.Http.Json;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;

namespace Trader.Infrastructure.Alerts;

public sealed class NtfyPublisher(HttpClient httpClient) : IAlertPublisher
{
    public async Task PublishAsync(NtfySettings settings, IReadOnlyList<AlertDto> alerts, CancellationToken cancellationToken)
    {
        foreach (AlertDto alert in alerts)
        {
            string? click = NtfyRouting.ClickUrl(settings, alert);

            var body = new
            {
                topic = settings.Topic,
                title = alert.Title,
                message = alert.Message,
                tags = new[] { TagFor(alert.Kind) },
                priority = NtfyRouting.IsCritical(alert.Kind) ? 4 : 3,
                click,
                actions = click is null ? null : new[] { new { action = "view", label = "Abrir no Trader", url = click, clear = true } }
            };

            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(settings.Server, body, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private static string TagFor(AlertKind kind) => kind switch
    {
        AlertKind.TakeProfit or AlertKind.TradeTakeProfit => "white_check_mark",
        AlertKind.StopLoss or AlertKind.TradeStopLoss => "x",
        AlertKind.PriceTarget => "bell",
        AlertKind.Calendar => "calendar",
        AlertKind.DailySummary => "newspaper",
        AlertKind.Expired => "hourglass",
        _ => "chart_with_upwards_trend"
    };
}
