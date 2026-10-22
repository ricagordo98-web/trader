using System.Globalization;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Explain;
using Trader.Core.Models;
using Trader.Infrastructure.News.Sources;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Explain;

internal static partial class EvidenceHelpers
{
    public static readonly TimeSpan LookBack = TimeSpan.FromHours(30);
    public static readonly TimeSpan LookAhead = TimeSpan.FromHours(3);
    public const int MaxItemsPerSource = 40;
    public const int MaxDetailLength = 220;

    [GeneratedRegex("^[A-Z]{1,5}$")]
    public static partial Regex UsTicker();

    public static bool IsUsEquity(EvidenceQuery query) => query.AssetClass == AssetClass.Stock && UsTicker().IsMatch(query.Symbol);

    public static DateTimeOffset Start(EvidenceQuery query) => query.From - LookBack;

    public static DateTimeOffset End(EvidenceQuery query) => query.To + LookAhead;

    public static bool MentionsSymbol(EvidenceQuery query, string text)
    {
        string ticker = query.Symbol.Split('.', '-', '=')[0];
        string firstName = query.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? query.Name;

        return text.Contains(query.Name, StringComparison.OrdinalIgnoreCase)
            || (firstName.Length >= 4 && text.Contains(firstName, StringComparison.OrdinalIgnoreCase))
            || (ticker.Length >= 3 && Regex.IsMatch(text, $@"\b{Regex.Escape(ticker)}\b"));
    }

    public static EvidenceItem FromArticle(EvidenceQuery query, string title, string summary, string url, string source, DateTimeOffset time, double reliability, double? sentiment, bool? mentions = null)
    {
        string text = $"{title} {summary}";
        CauseCategory category = CauseClassifier.Categorize(title);
        bool fromSummary = category == CauseCategory.Other && CauseClassifier.Categorize(summary) != CauseCategory.Other;

        if (fromSummary)
        {
            category = CauseClassifier.Categorize(summary);
        }

        string brief = summary.Length > MaxDetailLength ? summary[..(MaxDetailLength - 1)].TrimEnd() + "…" : summary;
        return new EvidenceItem(EvidenceKind.Article, category, title, brief, url, source, time, fromSummary ? reliability * 0.85 : reliability, sentiment, mentions ?? MentionsSymbol(query, text));
    }
}

public sealed class StoredArticleEvidence(TraderDbContext dbContext) : IEvidenceSource
{
    public string Name => "Notícias guardadas";

    public async Task<IReadOnlyList<EvidenceItem>> FetchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        DateTimeOffset start = EvidenceHelpers.Start(query);
        DateTimeOffset end = EvidenceHelpers.End(query);

        List<Article> articles = await (
            from link in dbContext.Set<ArticleSymbol>().AsNoTracking()
            join article in dbContext.Articles.AsNoTracking() on link.ArticleId equals article.Id
            where link.Symbol == query.Symbol && article.PublishedAt >= start && article.PublishedAt <= end
            select article)
            .Take(EvidenceHelpers.MaxItemsPerSource * 3)
            .ToListAsync(cancellationToken);

        return articles
            .OrderByDescending(a => Math.Abs(a.Sentiment ?? 0))
            .Take(EvidenceHelpers.MaxItemsPerSource)
            .Select(a => EvidenceHelpers.FromArticle(query, a.Title, a.Summary, a.Url, a.Source, a.PublishedAt, Reliability(a.SourceType), a.Sentiment))
            .ToList();
    }

    private static double Reliability(SourceType type) => type switch
    {
        SourceType.Social => 0.5,
        SourceType.Filing => 1.2,
        SourceType.Macro => 0.9,
        _ => 1.0
    };
}

public sealed class FinnhubNewsEvidence(HttpClient httpClient, IOptions<SourcesOptions> options, ILogger<FinnhubNewsEvidence> logger) : IEvidenceSource
{
    public string Name => "Finnhub";

    public async Task<IReadOnlyList<EvidenceItem>> FetchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        string key = options.Value.Finnhub.ApiKey;

        if (string.IsNullOrWhiteSpace(key) || !EvidenceHelpers.IsUsEquity(query))
        {
            return [];
        }

        string from = EvidenceHelpers.Start(query).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string to = EvidenceHelpers.End(query).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string json = await httpClient.GetStringAsync(
            $"https://finnhub.io/api/v1/company-news?symbol={Uri.EscapeDataString(query.Symbol)}&from={from}&to={to}&token={Uri.EscapeDataString(key)}",
            cancellationToken);

        DateTimeOffset start = EvidenceHelpers.Start(query);
        DateTimeOffset end = EvidenceHelpers.End(query);

        return FinnhubConnector.Parse(json)
            .Where(a => a.PublishedAt >= start && a.PublishedAt <= end)
            .OrderByDescending(a => a.PublishedAt)
            .Take(EvidenceHelpers.MaxItemsPerSource)
            .Select(a => EvidenceHelpers.FromArticle(query, a.Title, a.Summary, a.Url, a.Source, a.PublishedAt, 1.0, null))
            .ToList();
    }
}

public sealed class MarketauxEvidence(HttpClient httpClient, IOptions<SourcesOptions> options) : IEvidenceSource
{
    public string Name => "Marketaux";

    public async Task<IReadOnlyList<EvidenceItem>> FetchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        string key = options.Value.Marketaux.ApiKey;

        if (string.IsNullOrWhiteSpace(key) || !EvidenceHelpers.IsUsEquity(query))
        {
            return [];
        }

        string after = EvidenceHelpers.Start(query).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        string before = EvidenceHelpers.End(query).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        string json = await httpClient.GetStringAsync(
            $"https://api.marketaux.com/v1/news/all?symbols={Uri.EscapeDataString(query.Symbol)}&language=en&filter_entities=true&limit=20&published_after={after}&published_before={before}&api_token={Uri.EscapeDataString(key)}",
            cancellationToken);

        return MarketauxConnector.Parse(json)
            .Select(a => EvidenceHelpers.FromArticle(query, a.Title, a.Summary, a.Url, a.Source, a.PublishedAt, 1.0, null))
            .ToList();
    }
}

public sealed class GoogleNewsEvidence(HttpClient httpClient) : IEvidenceSource
{
    private const int MaxItems = 25;

    public string Name => "Google News";

    public async Task<IReadOnlyList<EvidenceItem>> FetchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        string after = EvidenceHelpers.Start(query).UtcDateTime.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string before = EvidenceHelpers.End(query).UtcDateTime.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string text = $"{SearchTerm(query)} after:{after} before:{before}";
        string url = $"https://news.google.com/rss/search?q={Uri.EscapeDataString(text)}&{Edition(query.Symbol)}";

        using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        SyndicationFeed feed = SyndicationFeed.Load(reader);

        DateTimeOffset start = EvidenceHelpers.Start(query);
        DateTimeOffset end = EvidenceHelpers.End(query);

        return feed.Items
            .Where(i => i.PublishDate >= start && i.PublishDate <= end && i.Links.Count > 0)
            .Take(MaxItems)
            .Select(i =>
            {
                string title = TextCleaner.Clean(i.Title?.Text, 300);
                string source = i.SourceFeed?.Title?.Text ?? i.ElementExtensions.ReadElementExtensions<string>("source", string.Empty).FirstOrDefault() ?? "Google News";
                int dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
                string cleanTitle = dash > 20 ? title[..dash] : title;
                string outlet = dash > 20 ? title[(dash + 3)..] : source;

                return EvidenceHelpers.FromArticle(query, cleanTitle, string.Empty, i.Links[0].Uri.ToString(), outlet, i.PublishDate, 0.9, null);
            })
            .ToList();
    }

    private static string SearchTerm(EvidenceQuery query) => query.AssetClass switch
    {
        AssetClass.Crypto => $"\"{query.Name}\" crypto price",
        AssetClass.Commodity => $"\"{query.Name}\" prices",
        AssetClass.Forex => $"{query.Name} forex",
        AssetClass.Index => $"\"{query.Name}\" stocks",
        _ => $"\"{query.Name}\" stock"
    };

    private static string Edition(string symbol) => symbol.EndsWith(".LS", StringComparison.OrdinalIgnoreCase)
        ? "hl=pt-PT&gl=PT&ceid=PT:pt-150"
        : "hl=en-US&gl=US&ceid=US:en";
}

public sealed class EarningsEvidence(HttpClient httpClient, IOptions<SourcesOptions> options) : IEvidenceSource
{
    public string Name => "Resultados (Finnhub)";

    public async Task<IReadOnlyList<EvidenceItem>> FetchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        string key = options.Value.Finnhub.ApiKey;

        if (string.IsNullOrWhiteSpace(key) || !EvidenceHelpers.IsUsEquity(query))
        {
            return [];
        }

        string from = EvidenceHelpers.Start(query).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string to = EvidenceHelpers.End(query).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string json = await httpClient.GetStringAsync(
            $"https://finnhub.io/api/v1/calendar/earnings?symbol={Uri.EscapeDataString(query.Symbol)}&from={from}&to={to}&token={Uri.EscapeDataString(key)}",
            cancellationToken);

        return Parse(query, json);
    }

    internal static IReadOnlyList<EvidenceItem> Parse(EvidenceQuery query, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("earningsCalendar", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<EvidenceItem> items = [];

        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (!DateOnly.TryParseExact(row.TryGetProperty("date", out JsonElement d) ? d.GetString() : null, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                continue;
            }

            double? actual = Number(row, "epsActual");
            double? estimate = Number(row, "epsEstimate");

            if (actual is null)
            {
                continue;
            }

            string hour = row.TryGetProperty("hour", out JsonElement h) ? h.GetString() ?? string.Empty : string.Empty;
            (int utcHour, string when) = hour switch
            {
                "bmo" => (11, "antes da abertura"),
                "amc" => (21, "depois do fecho"),
                _ => (15, "durante a sessão")
            };

            string detail = Describe(actual.Value, estimate, Number(row, "revenueActual"), Number(row, "revenueEstimate"), when);
            string title = $"{query.Name} divulgou resultados: EPS {Format(actual.Value)}{(estimate is { } e ? $" vs {Format(e)} esperado" : string.Empty)}";

            items.Add(new EvidenceItem(
                EvidenceKind.Earnings,
                CauseCategory.Earnings,
                title,
                detail,
                $"https://finance.yahoo.com/quote/{Uri.EscapeDataString(query.Symbol)}/analysis",
                "Finnhub (calendário de resultados)",
                new DateTimeOffset(date.ToDateTime(new TimeOnly(utcHour, 0)), TimeSpan.Zero),
                1.3,
                estimate is { } est && Math.Abs(est) > 1e-9 ? Math.Sign(actual.Value - est) * Math.Min(1, Math.Abs(actual.Value - est) / Math.Abs(est)) : null,
                true));
        }

        return items;
    }

    private static string Describe(double actual, double? estimate, double? revenue, double? revenueEstimate, string when)
    {
        List<string> parts = [when];

        if (estimate is { } eps && Math.Abs(eps) > 1e-9)
        {
            double surprise = (actual - eps) / Math.Abs(eps) * 100;
            parts.Add($"surpresa de EPS {surprise.ToString("+0.0;-0.0", CultureInfo.GetCultureInfo("pt-PT"))}%");
        }

        if (revenue is { } rev && revenueEstimate is { } revEst && Math.Abs(revEst) > 1e-9)
        {
            parts.Add($"receitas {((rev - revEst) / Math.Abs(revEst) * 100).ToString("+0.0;-0.0", CultureInfo.GetCultureInfo("pt-PT"))}% face ao esperado");
        }

        return string.Join("; ", parts);
    }

    private static double? Number(JsonElement row, string name) =>
        row.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;

    private static string Format(double value) => value.ToString("0.00", CultureInfo.GetCultureInfo("pt-PT"));
}

public sealed class EdgarEvidence(HttpClient httpClient, IMemoryCache cache, IOptions<SourcesOptions> options) : IEvidenceSource
{
    private const int MaxInsiderFilings = 2;
    private static readonly TimeSpan TickerMapDuration = TimeSpan.FromHours(24);

    private static readonly Dictionary<string, (string Description, CauseCategory Category)> ItemMeanings = new()
    {
        ["1.01"] = ("acordo material celebrado", CauseCategory.Product),
        ["1.02"] = ("acordo material terminado", CauseCategory.Product),
        ["2.01"] = ("aquisição ou venda de ativos concluída", CauseCategory.MergerAcquisition),
        ["2.02"] = ("resultados operacionais", CauseCategory.Earnings),
        ["2.05"] = ("custos de reestruturação", CauseCategory.Guidance),
        ["2.06"] = ("imparidade material", CauseCategory.Earnings),
        ["3.01"] = ("aviso de cotação da bolsa", CauseCategory.Legal),
        ["4.02"] = ("demonstrações financeiras não fiáveis", CauseCategory.Legal),
        ["5.01"] = ("mudança de controlo", CauseCategory.MergerAcquisition),
        ["5.02"] = ("saída ou nomeação de administrador ou executivo", CauseCategory.Management),
        ["5.03"] = ("alteração de estatutos", CauseCategory.Filing),
        ["7.01"] = ("divulgação Reg FD", CauseCategory.Filing),
        ["8.01"] = ("outros eventos", CauseCategory.Filing)
    };

    public string Name => "SEC EDGAR";

    public async Task<IReadOnlyList<EvidenceItem>> FetchAsync(EvidenceQuery query, CancellationToken cancellationToken)
    {
        string email = options.Value.Edgar.ContactEmail;

        if (string.IsNullOrWhiteSpace(email) || !EvidenceHelpers.IsUsEquity(query))
        {
            return [];
        }

        string? cik = await FindCikAsync(query.Symbol, email, cancellationToken);

        if (cik is null)
        {
            return [];
        }

        string json = await GetAsync($"https://data.sec.gov/submissions/CIK{cik}.json", email, cancellationToken);
        return Parse(query, cik, json);
    }

    internal static IReadOnlyList<EvidenceItem> Parse(EvidenceQuery query, string cik, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("filings", out JsonElement filings)
            || !filings.TryGetProperty("recent", out JsonElement recent)
            || !recent.TryGetProperty("form", out JsonElement forms))
        {
            return [];
        }

        DateTimeOffset start = EvidenceHelpers.Start(query);
        DateTimeOffset end = EvidenceHelpers.End(query);
        List<EvidenceItem> items = [];
        int insiders = 0;

        for (int i = 0; i < forms.GetArrayLength(); i++)
        {
            string form = forms[i].GetString() ?? string.Empty;
            string? accepted = At(recent, "acceptanceDateTime", i);

            if (!DateTimeOffset.TryParse(accepted, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time) || time < start || time > end)
            {
                continue;
            }

            string accession = (At(recent, "accessionNumber", i) ?? string.Empty).Replace("-", string.Empty);
            string document2 = At(recent, "primaryDocument", i) ?? string.Empty;
            string url = $"https://www.sec.gov/Archives/edgar/data/{cik.TrimStart('0')}/{accession}/{document2}";

            if (form.StartsWith("8-K", StringComparison.Ordinal))
            {
                string[] codes = (At(recent, "items", i) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                List<string> described = codes.Where(ItemMeanings.ContainsKey).Select(c => $"item {c}: {ItemMeanings[c].Description}").ToList();
                CauseCategory category = codes.Where(ItemMeanings.ContainsKey).Select(c => ItemMeanings[c].Category).FirstOrDefault(CauseCategory.Filing);

                items.Add(new EvidenceItem(
                    EvidenceKind.Filing,
                    category,
                    $"{query.Name} apresentou um {form} à SEC",
                    described.Count > 0 ? string.Join("; ", described) : (At(recent, "primaryDocDescription", i) ?? string.Empty),
                    url,
                    "SEC EDGAR",
                    time,
                    1.2,
                    null,
                    true));
            }
            else if ((form is "4" or "4/A") && insiders < MaxInsiderFilings)
            {
                insiders++;
                items.Add(new EvidenceItem(EvidenceKind.Filing, CauseCategory.Insider, $"Insider de {query.Name} comunicou uma transação (Form 4)", "transação de administrador ou acionista relevante", url, "SEC EDGAR", time, 1.0, null, true));
            }
        }

        return items;
    }

    private async Task<string?> FindCikAsync(string symbol, string email, CancellationToken cancellationToken)
    {
        Dictionary<string, string>? map = await cache.GetOrCreateAsync("edgar:tickers", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TickerMapDuration;
            string json = await GetAsync("https://www.sec.gov/files/company_tickers.json", email, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(json);

            return document.RootElement.EnumerateObject()
                .Select(p => p.Value)
                .Where(v => v.TryGetProperty("ticker", out _) && v.TryGetProperty("cik_str", out _))
                .GroupBy(v => v.GetProperty("ticker").GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().GetProperty("cik_str").GetInt64().ToString("D10", CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        });

        return map is not null && map.TryGetValue(symbol.Replace('-', '.'), out string? cik) ? cik : null;
    }

    private async Task<string> GetAsync(string url, string email, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", $"Trader personal research tool {email}");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string? At(JsonElement recent, string name, int index) =>
        recent.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array && index < array.GetArrayLength()
            ? array[index].GetString()
            : null;
}
