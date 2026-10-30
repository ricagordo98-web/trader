using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.News.Sources;

public sealed record ApeWisdomEntry(int Rank, string Ticker, string Name, int Mentions, int Upvotes, int? RankPrevious, int? MentionsPrevious);

public static class ApeWisdomParser
{
    public static IReadOnlyList<ApeWisdomEntry> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<ApeWisdomEntry> entries = [];

        foreach (JsonElement item in document.RootElement.GetProperty("results").EnumerateArray())
        {
            string? ticker = item.GetProperty("ticker").GetString();

            if (string.IsNullOrWhiteSpace(ticker))
            {
                continue;
            }

            entries.Add(new ApeWisdomEntry(
                Number(item, "rank") ?? 0,
                ticker,
                WebUtility.HtmlDecode(item.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? ticker : ticker),
                Number(item, "mentions") ?? 0,
                Number(item, "upvotes") ?? 0,
                Number(item, "rank_24h_ago"),
                Number(item, "mentions_24h_ago")));
        }

        return entries;
    }

    private static int? Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out int number) ? number : null,
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null,
            _ => null
        };
    }
}

public sealed class ApeWisdomClient(HttpClient httpClient, IMemoryCache cache, ILogger<ApeWisdomClient> logger)
{
    private const string CacheKey = "apewisdom:trending";

    public async Task<IReadOnlyList<ApeWisdomEntry>> GetTrendingAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<ApeWisdomEntry>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            IReadOnlyList<ApeWisdomEntry> entries = ApeWisdomParser.Parse(
                await httpClient.GetStringAsync("https://apewisdom.io/api/v1.0/filter/all-stocks/page/1", cancellationToken));
            cache.Set(CacheKey, entries, TimeSpan.FromMinutes(15));
            return entries;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("ApeWisdom failed: {Message}", ex.Message);
            return [];
        }
    }
}

public sealed class ApeWisdomConnector(ApeWisdomClient client, ISymbolProvider symbolProvider, IOptions<SourcesOptions> options) : ISourceConnector
{
    private readonly ApeWisdomOptions _options = options.Value.ApeWisdom;

    public string Name => "ApeWisdom";

    public TimeSpan Interval => TimeSpan.FromMinutes(_options.IntervalMinutes);

    public bool IsEnabled => _options.Enabled;

    public async Task<IReadOnlyList<RawArticle>> FetchAsync(CancellationToken cancellationToken) =>
        Spikes(await client.GetTrendingAsync(cancellationToken), await symbolProvider.GetSymbolsAsync(cancellationToken), _options, DateTimeOffset.UtcNow);

    internal static IReadOnlyList<RawArticle> Spikes(IReadOnlyList<ApeWisdomEntry> entries, IReadOnlyList<string> watchlist, ApeWisdomOptions options, DateTimeOffset now)
    {
        HashSet<string> symbols = watchlist.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return entries
            .Where(e => symbols.Contains(e.Ticker)
                && e.Mentions >= options.MinMentions
                && e.Mentions >= options.SpikeFactor * Math.Max(e.MentionsPrevious ?? 0, 1))
            .Select(e => new RawArticle(
                $"https://apewisdom.io/stocks/{e.Ticker}/?day={now:yyyyMMdd}",
                $"{e.Ticker} mentions on Reddit jump to {e.Mentions} in 24 hours (previous day: {e.MentionsPrevious ?? 0})",
                $"{e.Name} is now number {e.Rank} among the most discussed stocks on Reddit with {e.Upvotes} upvotes.",
                "ApeWisdom",
                SourceType.Social,
                now,
                [e.Ticker.ToUpperInvariant()]))
            .ToList();
    }
}
