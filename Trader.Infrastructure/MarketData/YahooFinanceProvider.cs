using System.Globalization;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.MarketData;

public sealed class YahooFinanceProvider(HttpClient httpClient) : IMarketDataProvider
{
    public async Task<ChartSeries> GetChartAsync(string symbol, string interval, string range, CancellationToken cancellationToken)
    {
        string url = string.Create(
            CultureInfo.InvariantCulture,
            $"v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval={interval}&range={range}&includePrePost=false");

        using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return YahooChartParser.Parse(json);
    }
}
