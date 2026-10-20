using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed class FredConnector(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<FredConnector> logger) : ISourceConnector
{
    internal sealed record Series(string Id, string Name, string Unit, bool YearOverYear);

    private static readonly Series[] AllSeries =
    [
        new("FEDFUNDS", "Taxa dos Fed Funds", "%", false),
        new("CPIAUCSL", "Inflação anual (CPI, EUA)", "%", true),
        new("UNRATE", "Taxa de desemprego (EUA)", "%", false),
        new("DGS10", "Yield das obrigações a 10 anos (EUA)", "%", false),
        new("T10Y2Y", "Spread de yields 10 anos menos 2 anos", " pp", false),
        new("DCOILWTICO", "Petróleo WTI", " USD", false)
    ];

    private readonly FredOptions _options = options.Value.Fred;

    public string Name => "FRED";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string? DisabledReason => IsEnabled ? null : "Falta a chave da API (Sources:Fred:ApiKey).";

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken)
    {
        List<RawArticle> articles = [];

        foreach (Series series in AllSeries)
        {
            try
            {
                string json = await httpClient.GetStringAsync(
                    $"https://api.stlouisfed.org/fred/series/observations?series_id={series.Id}&api_key={Uri.EscapeDataString(_options.ApiKey)}&file_type=json&sort_order=desc&limit=14",
                    cancellationToken);

                if (Parse(series, json, DateTimeOffset.UtcNow) is { } article)
                {
                    articles.Add(article);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("FRED series {Series} failed: {Message}", series.Id, ex.Message);
            }
        }

        return articles;
    }

    internal static RawArticle? Parse(Series series, string json, DateTimeOffset now)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("observations", out JsonElement observations))
        {
            return null;
        }

        List<(string Date, double Value)> values = [];

        foreach (JsonElement observation in observations.EnumerateArray())
        {
            if (double.TryParse(observation.GetProperty("value").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                values.Add((observation.GetProperty("date").GetString() ?? string.Empty, value));
            }
        }

        if (values.Count < 2)
        {
            return null;
        }

        string text;

        if (series.YearOverYear)
        {
            if (values.Count < 13)
            {
                return null;
            }

            double yearly = (values[0].Value / values[12].Value - 1) * 100;
            double previousYearly = (values[1].Value / (values.Count > 13 ? values[13].Value : values[12].Value) - 1) * 100;
            text = $"{series.Name}: {Format(yearly)}{series.Unit} (anterior {Format(previousYearly)}{series.Unit})";
        }
        else
        {
            text = $"{series.Name}: {Format(values[0].Value)}{series.Unit} (anterior {Format(values[1].Value)})";
        }

        return new RawArticle(
            $"https://fred.stlouisfed.org/series/{series.Id}?obs={values[0].Date}",
            text,
            string.Empty,
            "FRED",
            SourceType.Macro,
            now);
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("pt-PT"));
}
