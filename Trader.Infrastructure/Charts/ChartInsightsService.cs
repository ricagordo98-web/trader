using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Charts;
using Trader.Core.Explain;
using Trader.Core.Journal;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Charts;

public sealed class ChartInsightsService(
    TraderDbContext dbContext,
    IMarketDataService marketData,
    IJournalService journal,
    IEnumerable<IEvidenceSource> evidenceSources) : IChartInsights
{
    private const int MaxNews = 25;
    private const double MinNewsSentiment = 0.3;

    public async Task<IReadOnlyList<AutoLevel>> GetLevelsAsync(string symbol, CancellationToken cancellationToken)
    {
        IReadOnlyList<Candle> daily = await marketData.GetCandlesAsync(symbol, "1d", "3mo", cancellationToken);
        IReadOnlyList<Candle> intraday = [];

        try
        {
            intraday = await marketData.GetCandlesAsync(symbol, "5m", "5d", cancellationToken);
        }
        catch (HttpRequestException)
        {
        }

        return AutoLevelCalculator.Compute(daily, intraday);
    }

    public async Task<IReadOnlyList<ChartEventDto>> GetEventsAsync(string symbol, string userId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        List<ChartEventDto> events = [];

        List<Article> articles = await (
            from link in dbContext.Set<ArticleSymbol>().AsNoTracking()
            join article in dbContext.Articles.AsNoTracking() on link.ArticleId equals article.Id
            where link.Symbol == symbol && article.PublishedAt >= start && article.PublishedAt <= end && article.Sentiment != null
            select article)
            .ToListAsync(cancellationToken);

        events.AddRange(articles
            .Where(a => Math.Abs(a.Sentiment!.Value) >= MinNewsSentiment)
            .OrderByDescending(a => Math.Abs(a.Sentiment!.Value))
            .Take(MaxNews)
            .Select(a => new ChartEventDto(a.PublishedAt, "news", "Notícia", $"{a.Title} ({a.Source})", a.Url)));

        List<Suggestion> suggestions = await dbContext.Suggestions
            .AsNoTracking()
            .Where(s => !s.IsShadow && s.Symbol == symbol && s.CreatedAt >= start && s.CreatedAt <= end)
            .ToListAsync(cancellationToken);

        events.AddRange(suggestions.Select(s => new ChartEventDto(
            s.CreatedAt,
            "suggestion",
            s.Direction == TradeDirection.Buy ? "Sugestão compra" : "Sugestão venda",
            $"{(s.Confidence * 100):0}% de certeza · {s.Status}",
            null)));

        IReadOnlyList<JournalTradeDto> trades = await journal.ListAsync(userId, null, cancellationToken);

        foreach (JournalTradeDto trade in trades.Where(t => t.Symbol == symbol))
        {
            if (trade.EntryTime >= start && trade.EntryTime <= end)
            {
                events.Add(new ChartEventDto(trade.EntryTime, "trade", "Entrada", $"{(trade.Direction == TradeDirection.Buy ? "Compra" : "Venda")} a {trade.EntryPrice:0.#####}", null));
            }

            if (trade.ExitTime is { } exit && exit >= start && exit <= end)
            {
                events.Add(new ChartEventDto(exit, "trade", "Saída", $"a {trade.ExitPrice:0.#####}", null));
            }
        }

        Instrument? instrument = await dbContext.Instruments.AsNoTracking().FirstOrDefaultAsync(i => i.Symbol == symbol, cancellationToken);

        if (instrument is not null && evidenceSources.FirstOrDefault(e => e.Name.StartsWith("Resultados", StringComparison.Ordinal)) is { } earnings)
        {
            try
            {
                IReadOnlyList<EvidenceItem> items = await earnings.FetchAsync(new EvidenceQuery(symbol, instrument.Name, instrument.AssetClass, start, end), cancellationToken);
                events.AddRange(items.Where(i => i.Time >= start && i.Time <= end).Select(i => new ChartEventDto(i.Time, "earnings", "Resultados", i.Title, i.Url)));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
            }
        }

        return events.OrderBy(e => e.Time).ToList();
    }
}

public sealed class ChartLayoutService(TraderDbContext dbContext) : IChartLayoutService
{
    private const int MaxLayouts = 20;
    private const int MaxJsonLength = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ChartLayoutDto>> ListAsync(string userId, CancellationToken cancellationToken) =>
        (await LoadAsync(userId, cancellationToken)).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public async Task<IReadOnlyList<string>> SaveAsync(string userId, string name, string json, CancellationToken cancellationToken)
    {
        string clean = name.Trim();

        if (clean.Length is 0 or > 40)
        {
            return ["Dá um nome à disposição (até 40 caracteres)."];
        }

        if (json.Length > MaxJsonLength)
        {
            return ["A disposição é demasiado grande."];
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return ["Disposição inválida."];
        }

        List<ChartLayoutDto> layouts = (await LoadAsync(userId, cancellationToken)).ToList();
        layouts.RemoveAll(l => l.Name.Equals(clean, StringComparison.OrdinalIgnoreCase));

        if (layouts.Count >= MaxLayouts)
        {
            return [$"Máximo de {MaxLayouts} disposições guardadas."];
        }

        layouts.Add(new ChartLayoutDto(clean, json, DateTimeOffset.UtcNow));
        await StoreAsync(userId, layouts, cancellationToken);
        return [];
    }

    public async Task<bool> DeleteAsync(string userId, string name, CancellationToken cancellationToken)
    {
        List<ChartLayoutDto> layouts = (await LoadAsync(userId, cancellationToken)).ToList();

        if (layouts.RemoveAll(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        await StoreAsync(userId, layouts, cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<ChartLayoutDto>> LoadAsync(string userId, CancellationToken cancellationToken)
    {
        string? json = await dbContext.AppSettings.AsNoTracking().Where(s => s.Key == Key(userId)).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);

        try
        {
            return json is null ? [] : JsonSerializer.Deserialize<List<ChartLayoutDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task StoreAsync(string userId, List<ChartLayoutDto> layouts, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(layouts, JsonOptions);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == Key(userId), cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = Key(userId), Value = json });
        }
        else
        {
            setting.Value = json;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Key(string userId) => $"chart-layouts:{userId}";
}
