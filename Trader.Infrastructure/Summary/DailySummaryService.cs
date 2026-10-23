using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Calendar;
using Trader.Core.Journal;
using Trader.Core.Explain;
using Trader.Core.Models;
using Trader.Core.Summary;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Summary;

public sealed class SummaryOptions
{
    public const string SectionName = "Summary";

    public int GenerateHour { get; set; } = 8;

    public string OllamaUrl { get; set; } = "http://localhost:11434";

    public string OllamaModel { get; set; } = string.Empty;
}

public sealed class OllamaNarrator(HttpClient httpClient, IOptions<SummaryOptions> options, ILogger<OllamaNarrator> logger) : ISummaryNarrator
{
    private readonly SummaryOptions _options = options.Value;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.OllamaModel);

    public string Name => $"modelo local ({_options.OllamaModel})";

    public async Task<string?> NarrateAsync(DailySummary summary, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                $"{_options.OllamaUrl.TrimEnd('/')}/api/generate",
                new { model = _options.OllamaModel, prompt = SummaryBuilder.Prompt(summary), stream = false },
                cancellationToken);
            response.EnsureSuccessStatusCode();

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            string? text = document.RootElement.TryGetProperty("response", out JsonElement element) ? element.GetString()?.Trim() : null;

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Local model summary failed, using the automatic summary: {Message}", ex.Message);
            return null;
        }
    }
}

public sealed class DailySummaryService(
    TraderDbContext dbContext,
    IMarketDataService marketData,
    INewsService news,
    ICalendarService calendar,
    ISummaryNarrator narrator,
    IMoveExplainer explainer,
    IJournalService journal,
    ILogger<DailySummaryService> logger) : IDailySummaryService
{
    private const string SettingKey = "summary.last";
    private const int MoversToExplain = 3;
    private static readonly TimeSpan ExplainTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DailySummary> GenerateAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset since = now.AddHours(-24);

        IReadOnlyList<InstrumentQuote> quotes = await SafeAsync(() => marketData.GetQuotesAsync(null, cancellationToken), []);
        IReadOnlyList<SentimentSummary> sentiment = await news.GetSentimentSummaryAsync(TimeSpan.FromHours(24), cancellationToken);
        CalendarFeed feed = await SafeAsync(() => calendar.GetAsync(3, cancellationToken), new CalendarFeed([], []));

        List<Suggestion> created = await dbContext.Suggestions.AsNoTracking().Where(s => s.CreatedAt >= since).ToListAsync(cancellationToken);
        List<Suggestion> closed = await dbContext.Suggestions.AsNoTracking().Where(s => s.ClosedAt != null && s.ClosedAt >= since).ToListAsync(cancellationToken);

        List<HeadlineItem> headlines = await LoadHeadlinesAsync(since, cancellationToken);

        IReadOnlyList<MoverReason> reasons = await ExplainTopMoversAsync(quotes, cancellationToken);

        SummaryInput input = new(quotes, sentiment, created.Select(ToDto).ToList(), closed.Select(ToDto).ToList(), headlines, feed.Events, DateOnly.FromDateTime(DateTime.Now), now, reasons);
        DailySummary summary = SummaryBuilder.Build(input);

        if (narrator.IsEnabled && await narrator.NarrateAsync(summary, cancellationToken) is { } narrative)
        {
            summary = summary with { Narrative = narrative, Source = narrator.Name };
        }

        await SaveAsync(summary, cancellationToken);
        logger.LogInformation("Daily summary generated ({Source})", summary.Source);
        return summary;
    }

    public async Task<DailySummary> GenerateWeeklyAsync(string userId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset since = now.AddDays(-7);

        IReadOnlyList<InstrumentQuote> quotes = await SafeAsync(() => marketData.GetQuotesAsync(null, cancellationToken), []);
        CalendarFeed feed = await SafeAsync(() => calendar.GetAsync(8, cancellationToken), new CalendarFeed([], []));
        List<Suggestion> closed = await dbContext.Suggestions.AsNoTracking().Where(s => s.ClosedAt != null && s.ClosedAt >= since).ToListAsync(cancellationToken);
        IReadOnlyList<JournalTradeDto> trades = (await journal.ListAsync(userId, TradeStatus.Closed, cancellationToken))
            .Where(t => t.ExitTime is { } exit && exit >= since)
            .ToList();

        return WeeklySummaryBuilder.Build(new WeeklyInput(quotes, closed.Select(ToDto).ToList(), trades, feed.Events, DateOnly.FromDateTime(DateTime.Now), now));
    }

    public async Task<DailySummary?> GetLastAsync(CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);

        return setting is null ? null : JsonSerializer.Deserialize<DailySummary>(setting.Value, JsonOptions);
    }

    private async Task<IReadOnlyList<MoverReason>> ExplainTopMoversAsync(IReadOnlyList<InstrumentQuote> quotes, CancellationToken cancellationToken)
    {
        List<MoverReason> reasons = [];

        foreach (InstrumentQuote quote in quotes
            .Where(q => q.ChangePercent is not null && q.AssetClass is not (AssetClass.Forex or AssetClass.Index))
            .OrderByDescending(q => Math.Abs(q.ChangePercent!.Value))
            .Take(MoversToExplain))
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ExplainTimeout);

            try
            {
                MoveExplanation? explanation = await explainer.ExplainAsync(new MoveRequest(quote.Symbol, null, null), timeout.Token);
                string? text = explanation is null ? null : explanation.Narrative is { } narrative ? ExplanationText.WithoutCitations(narrative) : ExplanationText.OneLiner(explanation);

                if (text is not null)
                {
                    reasons.Add(new MoverReason(quote.Symbol, quote.ChangePercent!.Value, text));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Could not explain {Symbol} for the summary: {Message}", quote.Symbol, ex.Message);
            }
        }

        return reasons;
    }

    private async Task<List<HeadlineItem>> LoadHeadlinesAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        List<Article> articles = await dbContext.Articles
            .AsNoTracking()
            .Include(a => a.Symbols)
            .Where(a => a.PublishedAt >= since && a.Sentiment != null && a.Symbols.Count > 0)
            .ToListAsync(cancellationToken);

        return articles
            .OrderByDescending(a => Math.Abs(a.Sentiment!.Value) * (a.SourceType == SourceType.News ? 1.0 : 0.6))
            .Take(5)
            .Select(a => new HeadlineItem(a.Title, a.Source, a.Sentiment, a.Symbols.Select(s => s.Symbol).ToList()))
            .ToList();
    }

    private async Task SaveAsync(DailySummary summary, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(summary, JsonOptions);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = SettingKey, Value = json });
        }
        else
        {
            setting.Value = json;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<T> SafeAsync<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Summary input unavailable: {Message}", ex.Message);
            return fallback;
        }
    }

    private static SuggestionDto ToDto(Suggestion s) => new(
        s.Id, s.Symbol, s.Name, s.Direction, s.Confidence, s.Entry, s.StopLoss, s.TakeProfit, s.RiskReward, s.Explanation,
        s.CreatedAt, s.ExpiresAt, s.Status, s.ClosedAt, s.ExitPrice, s.RMultiple, [], []);
}
