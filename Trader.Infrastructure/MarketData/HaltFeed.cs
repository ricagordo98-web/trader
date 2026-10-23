using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Market;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.MarketData;

public static class HaltParser
{
    private static readonly TimeZoneInfo Eastern = MarketSessions.Zone("America/New_York", "Eastern Standard Time");

    public static IReadOnlyList<TradingHalt> Parse(string xml)
    {
        XDocument document = XDocument.Parse(xml);
        List<TradingHalt> halts = [];

        foreach (XElement item in document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            string symbol = Value(item, "IssueSymbol");

            if (symbol.Length == 0)
            {
                continue;
            }

            DateTimeOffset? halted = Combine(Value(item, "HaltDate"), Value(item, "HaltTime"));
            DateTimeOffset? resumed = Combine(Value(item, "ResumptionDate"), FirstNonEmpty(Value(item, "ResumptionTradeTime"), Value(item, "ResumptionQuoteTime")));
            string code = Value(item, "ReasonCode");

            if (halted is null)
            {
                continue;
            }

            halts.Add(new TradingHalt(symbol.ToUpperInvariant(), Value(item, "IssueName"), Value(item, "Market"), code, HaltReasons.Describe(code), halted.Value, resumed));
        }

        return halts;
    }

    private static string Value(XElement item, string name) =>
        item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? string.Empty;

    private static string FirstNonEmpty(string a, string b) => a.Length > 0 ? a : b;

    private static DateTimeOffset? Combine(string date, string time)
    {
        if (date.Length == 0 || time.Length == 0)
        {
            return null;
        }

        string[] dateFormats = ["MM/dd/yyyy", "M/d/yyyy"];
        string[] timeFormats = ["HH:mm:ss", "H:mm:ss", "HH:mm:ss.fff"];

        if (!DateTime.TryParseExact(date, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day)
            || !DateTime.TryParseExact(time, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime clock))
        {
            return null;
        }

        DateTime local = DateTime.SpecifyKind(day.Date + clock.TimeOfDay, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Eastern.GetUtcOffset(local)).ToUniversalTime();
    }
}

public sealed class HaltMonitor
{
    private readonly object _gate = new();
    private IReadOnlyList<TradingHalt> _halts = [];
    private HashSet<string> _alerted = [];

    public IReadOnlyList<TradingHalt> Halts
    {
        get
        {
            lock (_gate)
            {
                return _halts;
            }
        }
    }

    public IReadOnlyList<TradingHalt> Update(IReadOnlyList<TradingHalt> halts, IReadOnlySet<string> watchlist)
    {
        lock (_gate)
        {
            _halts = halts.OrderByDescending(h => h.HaltedAt).ToList();
            List<TradingHalt> fresh = halts
                .Where(h => h.IsActive && watchlist.Contains(h.Symbol) && _alerted.Add($"{h.Symbol}|{h.HaltedAt:O}"))
                .ToList();
            return fresh;
        }
    }
}

public sealed class HaltWorker(IServiceScopeFactory scopeFactory, HaltMonitor monitor, ILogger<HaltWorker> logger) : BackgroundService
{
    private const string FeedUrl = "https://www.nasdaqtrader.com/rss.aspx?feed=tradehalts";
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);

            using PeriodicTimer timer = new(Interval);

            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            HttpClient client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HaltWorker));
            string xml = await client.GetStringAsync(FeedUrl, cancellationToken);
            IReadOnlyList<TradingHalt> halts = HaltParser.Parse(xml);

            TraderDbContext dbContext = scope.ServiceProvider.GetRequiredService<TraderDbContext>();
            HashSet<string> watchlist = (await dbContext.Instruments.AsNoTracking().Select(i => i.Symbol).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<TradingHalt> fresh = monitor.Update(halts, watchlist);

            if (fresh.Count > 0)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                List<Alert> alerts = fresh.Select(h => new Alert
                {
                    Kind = AlertKind.Halt,
                    Symbol = h.Symbol,
                    Title = $"Suspensão: {h.Symbol}",
                    Message = $"{h.Reason} · desde {h.HaltedAt.ToLocalTime():HH:mm}",
                    CreatedAt = now
                }).ToList();

                await scope.ServiceProvider.GetRequiredService<IAlertService>().RaiseAsync(alerts, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Trading halts feed unavailable: {Message}", ex.Message);
        }
    }
}
