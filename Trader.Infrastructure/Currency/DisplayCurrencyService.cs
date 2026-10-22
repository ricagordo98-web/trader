using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Currency;

public sealed class DisplayCurrencyService(
    TraderDbContext dbContext,
    IMarketDataProvider provider,
    IMemoryCache cache,
    ILogger<DisplayCurrencyService> logger) : IDisplayCurrency
{
    public const string Key = "display.currency";
    public static readonly IReadOnlyList<string> Supported = ["EUR", "USD", "GBP"];

    private const string ConversionCacheKey = "display:conversion";
    private static readonly TimeSpan ConversionCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FailedConversionCacheDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RateCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly Dictionary<string, (string Currency, double Unit)> SubUnits = new(StringComparer.Ordinal)
    {
        ["USc"] = ("USD", 0.01),
        ["GBp"] = ("GBP", 0.01),
        ["GBX"] = ("GBP", 0.01)
    };

    public async Task<string> GetAsync(CancellationToken cancellationToken) =>
        await dbContext.AppSettings.AsNoTracking().Where(s => s.Key == Key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

    public async Task<string?> SetAsync(string currency, CancellationToken cancellationToken)
    {
        string normalized = currency.Trim().ToUpperInvariant();

        if (normalized.Length > 0 && !Supported.Contains(normalized))
        {
            return $"Moeda não suportada. Usa uma destas: {string.Join(", ", Supported)}.";
        }

        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == Key, cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = Key, Value = normalized });
        }
        else
        {
            setting.Value = normalized;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<DisplayConversion> GetConversionAsync(CancellationToken cancellationToken) =>
        await GetConversionAsync(await GetAsync(cancellationToken), cancellationToken);

    public async Task<DisplayConversion> GetConversionAsync(string currency, CancellationToken cancellationToken)
    {
        string target = currency.Trim().ToUpperInvariant();
        string cacheKey = $"{ConversionCacheKey}:{target}";

        if (cache.TryGetValue(cacheKey, out DisplayConversion? cached) && cached is not null)
        {
            return cached;
        }

        if (target.Length == 0 || !Supported.Contains(target))
        {
            cache.Set(cacheKey, DisplayConversion.None, ConversionCacheDuration);
            return DisplayConversion.None;
        }

        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => i.AssetClass != AssetClass.Forex && i.AssetClass != AssetClass.Index)
            .Select(i => new { i.Symbol, i.Currency })
            .ToListAsync(cancellationToken);

        Dictionary<string, double> factors = [];
        bool complete = true;

        foreach (var group in instruments.GroupBy(i => i.Currency))
        {
            (string baseCurrency, double unit) = SubUnits.TryGetValue(group.Key, out (string Currency, double Unit) sub)
                ? sub
                : (group.Key.ToUpperInvariant(), 1);

            double? rate = baseCurrency == target ? 1 : await GetRateAsync(baseCurrency, target, cancellationToken);

            if (rate is null)
            {
                complete = false;
                continue;
            }

            foreach (var instrument in group)
            {
                factors[instrument.Symbol] = unit * rate.Value;
            }
        }

        DisplayConversion conversion = new(target, factors);
        cache.Set(cacheKey, conversion, complete ? ConversionCacheDuration : FailedConversionCacheDuration);
        return conversion;
    }

    private async Task<double?> GetRateAsync(string from, string to, CancellationToken cancellationToken)
    {
        string key = $"fx:{from}{to}";

        if (cache.TryGetValue(key, out double cachedRate))
        {
            return cachedRate;
        }

        double? rate = await QuoteAsync($"{from}{to}=X", cancellationToken);

        if (rate is null && await QuoteAsync($"{to}{from}=X", cancellationToken) is { } inverse)
        {
            rate = 1 / inverse;
        }

        if (rate is { } found)
        {
            cache.Set(key, found, RateCacheDuration);
        }

        return rate;
    }

    private async Task<double?> QuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            ChartSeries series = await provider.GetChartAsync(symbol, "1d", "5d", cancellationToken);
            double? price = series.LastPrice ?? series.Candles.LastOrDefault()?.Close;
            return price is > 0 ? price : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Could not load exchange rate {Symbol}: {Message}", symbol, ex.Message);
            return null;
        }
    }
}
