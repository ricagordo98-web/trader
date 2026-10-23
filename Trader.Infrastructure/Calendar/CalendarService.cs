using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Calendar;
using Trader.Core.Models;
using Trader.Infrastructure.News.Sources;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Calendar;

public sealed partial class CalendarService(
    HttpClient httpClient,
    TraderDbContext dbContext,
    IMemoryCache cache,
    IOptions<SourcesOptions> options,
    ILogger<CalendarService> logger) : ICalendarService
{
    internal sealed record MacroRelease(int Id, string Name);

    private static readonly MacroRelease[] MacroReleases =
    [
        new(10, "Inflação (CPI, EUA)"),
        new(50, "Emprego (Non-Farm Payrolls, EUA)"),
        new(53, "PIB (EUA)"),
        new(54, "Consumo e inflação PCE (EUA)"),
        new(46, "Preços na produção (PPI, EUA)"),
        new(9, "Vendas a retalho (EUA)")
    ];

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(3);
    private static readonly TimeSpan EarningsCacheDuration = TimeSpan.FromHours(12);
    private const int MaxUncachedEarningsPerCall = 40;

    // Decision days of the 2026 FOMC meetings, as published by the Federal Reserve.
    private static readonly DateOnly[] FomcDecisions =
    [
        new(2026, 1, 28), new(2026, 3, 18), new(2026, 4, 29), new(2026, 6, 17),
        new(2026, 7, 29), new(2026, 9, 16), new(2026, 10, 28), new(2026, 12, 9)
    ];

    public async Task<CalendarFeed> GetAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 60);
        string key = $"calendar:{days}:{DateTime.UtcNow:yyyyMMddHH}";

        if (cache.TryGetValue(key, out CalendarFeed? cached) && cached is not null)
        {
            return cached;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly end = today.AddDays(days);
        List<CalendarEvent> events = [];
        List<string> notes = [];

        string finnhubKey = options.Value.Finnhub.ApiKey;
        string fredKey = options.Value.Fred.ApiKey;

        if (string.IsNullOrWhiteSpace(finnhubKey))
        {
            notes.Add("Sem chave do Finnhub: os resultados das empresas não estão disponíveis.");
        }
        else
        {
            var instruments = await dbContext.Instruments.Select(i => new { i.Symbol, i.AssetClass }).ToListAsync(cancellationToken);
            HashSet<string> watchlist = instruments.Select(i => i.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<string> equities = instruments
                .Where(i => i.AssetClass == AssetClass.Stock && EquitySymbol().IsMatch(i.Symbol))
                .Select(i => i.Symbol)
                .ToList();

            events.AddRange(await LoadEarningsAsync(finnhubKey, today, end, watchlist, equities, notes, cancellationToken));
        }

        if (string.IsNullOrWhiteSpace(fredKey))
        {
            notes.Add("Sem chave do FRED: as datas de inflação, emprego e juros não estão disponíveis.");
        }
        else
        {
            events.AddRange(await LoadMacroAsync(fredKey, today, end, notes, cancellationToken));
        }

        events.AddRange(FomcDecisions
            .Where(date => date >= today && date <= end)
            .Select(date => new CalendarEvent(date, CalendarKind.Macro, null, "Decisão de juros da Fed (FOMC)", string.Empty)));

        CalendarFeed feed = new(events.OrderBy(e => e.Date).ThenBy(e => e.Kind).ThenBy(e => e.Symbol).ToList(), notes);

        if (notes.Count == 0)
        {
            cache.Set(key, feed, CacheDuration);
        }

        return feed;
    }

    private async Task<IEnumerable<CalendarEvent>> LoadEarningsAsync(string apiKey, DateOnly from, DateOnly to, HashSet<string> watchlist, List<string> equities, List<string> notes, CancellationToken cancellationToken)
    {
        List<CalendarEvent> events = [];
        List<string> missing = [];

        foreach (string symbol in equities)
        {
            if (cache.TryGetValue($"earnings:{symbol}:{from:yyyyMMdd}:{to:yyyyMMdd}", out IReadOnlyList<CalendarEvent>? cached) && cached is not null)
            {
                events.AddRange(cached);
            }
            else
            {
                missing.Add(symbol);
            }
        }

        using SemaphoreSlim gate = new(2);
        bool failed = false;
        bool rateLimited = false;

        IEnumerable<Task<IReadOnlyList<CalendarEvent>>> tasks = missing
            .Take(MaxUncachedEarningsPerCall)
            .Select(async symbol =>
            {
                await gate.WaitAsync(cancellationToken);

                try
                {
                    if (rateLimited)
                    {
                        return [];
                    }

                    string json = await httpClient.GetStringAsync(
                        $"https://finnhub.io/api/v1/calendar/earnings?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&symbol={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(apiKey)}",
                        cancellationToken);

                    IReadOnlyList<CalendarEvent> parsed = ParseEarnings(json, watchlist);
                    cache.Set($"earnings:{symbol}:{from:yyyyMMdd}:{to:yyyyMMdd}", parsed, EarningsCacheDuration);
                    return parsed;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests })
                    {
                        if (!rateLimited)
                        {
                            logger.LogWarning("Finnhub rate limit reached while loading earnings (first symbol {Symbol}); the rest will load on the next refresh.", symbol);
                        }

                        rateLimited = true;
                    }
                    else
                    {
                        logger.LogWarning("Earnings calendar for {Symbol} failed: {Message}", symbol, ex.Message);
                    }

                    failed = true;
                    return [];
                }
                finally
                {
                    gate.Release();
                }
            });

        events.AddRange((await Task.WhenAll(tasks)).SelectMany(r => r));

        if (failed)
        {
            notes.Add("Alguns resultados de empresas não puderam ser obtidos.");
        }

        if (missing.Count > MaxUncachedEarningsPerCall)
        {
            notes.Add($"A carregar os resultados de mais {missing.Count - MaxUncachedEarningsPerCall} empresas: atualiza daqui a um minuto.");
        }

        return events;
    }

    [GeneratedRegex("^[A-Z]{1,5}$")]
    private static partial Regex EquitySymbol();

    private async Task<IEnumerable<CalendarEvent>> LoadMacroAsync(string apiKey, DateOnly from, DateOnly to, List<string> notes, CancellationToken cancellationToken)
    {
        List<CalendarEvent> events = [];
        bool failed = false;

        foreach (MacroRelease release in MacroReleases)
        {
            try
            {
                string json = await httpClient.GetStringAsync(
                    $"https://api.stlouisfed.org/fred/release/dates?release_id={release.Id}&realtime_start={from:yyyy-MM-dd}&realtime_end=9999-12-31&include_release_dates_with_no_data=true&sort_order=asc&limit=10&file_type=json&api_key={Uri.EscapeDataString(apiKey)}",
                    cancellationToken);

                events.AddRange(ParseMacro(release, json, from, to));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("FRED release {Release} failed: {Message}", release.Id, ex.Message);
                failed = true;
            }
        }

        if (failed)
        {
            notes.Add("Algumas datas macro não puderam ser obtidas.");
        }

        return events;
    }

    internal static IReadOnlyList<CalendarEvent> ParseEarnings(string json, HashSet<string> watchlist)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("earningsCalendar", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<CalendarEvent> events = [];

        foreach (JsonElement row in rows.EnumerateArray())
        {
            string? symbol = row.TryGetProperty("symbol", out JsonElement s) ? s.GetString() : null;

            if (symbol is null || !watchlist.Contains(symbol)
                || !DateOnly.TryParseExact(row.TryGetProperty("date", out JsonElement d) ? d.GetString() : null, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                continue;
            }

            string hour = row.TryGetProperty("hour", out JsonElement h) ? h.GetString() ?? string.Empty : string.Empty;
            string when = hour switch
            {
                "bmo" => "antes da abertura",
                "amc" => "depois do fecho",
                "dmh" => "durante a sessão",
                _ => string.Empty
            };

            List<string> parts = [];

            if (when.Length > 0)
            {
                parts.Add(when);
            }

            if (row.TryGetProperty("epsEstimate", out JsonElement eps) && eps.ValueKind == JsonValueKind.Number)
            {
                parts.Add($"EPS esperado {eps.GetDouble().ToString("0.00", CultureInfo.GetCultureInfo("pt-PT"))}");
            }

            events.Add(new CalendarEvent(date, CalendarKind.Earnings, symbol.ToUpperInvariant(), $"Resultados de {symbol.ToUpperInvariant()}", string.Join(" · ", parts)));
        }

        return events;
    }

    internal static IReadOnlyList<CalendarEvent> ParseMacro(MacroRelease release, string json, DateOnly from, DateOnly to)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("release_dates", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rows.EnumerateArray()
            .Select(row => row.TryGetProperty("date", out JsonElement d) ? d.GetString() : null)
            .Select(text => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date : (DateOnly?)null)
            .Where(date => date is { } value && value >= from && value <= to)
            .Select(date => new CalendarEvent(date!.Value, CalendarKind.Macro, null, release.Name, string.Empty))
            .ToList();
    }
}
