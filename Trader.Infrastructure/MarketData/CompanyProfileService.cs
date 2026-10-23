using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Trader.Core.Market;
using Trader.Infrastructure.News.Sources;

namespace Trader.Infrastructure.MarketData;

public interface ICompanyProfileService
{
    Task<IReadOnlyList<CompanyProfile>> GetAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken);
}

public sealed partial class CompanyProfileService(HttpClient httpClient, IMemoryCache cache, IOptions<SourcesOptions> options) : ICompanyProfileService
{
    public const int MaxSymbols = 12;
    private static readonly TimeSpan HitDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan MissDuration = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<CompanyProfile>> GetAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        string key = options.Value.Finnhub.ApiKey;
        List<CompanyProfile> profiles = [];

        if (string.IsNullOrWhiteSpace(key))
        {
            return profiles;
        }

        foreach (string symbol in symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => UsTicker().IsMatch(s)).Distinct().Take(MaxSymbols))
        {
            string cacheKey = $"profile:{symbol}";

            if (!cache.TryGetValue(cacheKey, out CompanyProfile? profile))
            {
                profile = await FetchAsync(symbol, key, cancellationToken);
                cache.Set(cacheKey, profile, profile is null ? MissDuration : HitDuration);
            }

            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    internal static CompanyProfile? Parse(string symbol, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("shareOutstanding", out JsonElement shares) || shares.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        double? marketCap = root.TryGetProperty("marketCapitalization", out JsonElement cap) && cap.ValueKind == JsonValueKind.Number ? cap.GetDouble() : null;
        string industry = root.TryGetProperty("finnhubIndustry", out JsonElement industryElement) ? industryElement.GetString() ?? string.Empty : string.Empty;

        return new CompanyProfile(symbol, shares.GetDouble(), marketCap, industry);
    }

    private async Task<CompanyProfile?> FetchAsync(string symbol, string key, CancellationToken cancellationToken)
    {
        try
        {
            string json = await httpClient.GetStringAsync($"https://finnhub.io/api/v1/stock/profile2?symbol={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(key)}", cancellationToken);
            return Parse(symbol, json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Z]{1,5}$")]
    private static partial Regex UsTicker();
}
