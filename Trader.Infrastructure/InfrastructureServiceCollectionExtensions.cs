using Trader.Infrastructure.Alerts;
using Trader.Infrastructure.Backup;
using Trader.Infrastructure.Calendar;
using Trader.Infrastructure.Charts;
using Trader.Infrastructure.Summary;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Scoring;
using Trader.Infrastructure.Backtesting;
using Trader.Core.Intraday;
using Trader.Infrastructure.Intraday;
using Trader.Infrastructure.Journal;
using Trader.Infrastructure.MarketData;
using Trader.Infrastructure.News;
using Trader.Infrastructure.News.Sources;
using Trader.Infrastructure.Persistence;
using Trader.Infrastructure.Suggestions;

namespace Trader.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Trader/1.0";

    public static IServiceCollection AddTraderInfrastructure(this IServiceCollection services, IConfiguration configuration, string finBertDirectory)
    {
        services.AddDbContext<TraderDbContext>(options => options.UseSqlite(configuration.GetConnectionString("Trader")));
        services.AddMemoryCache();

        services.AddHttpClient<IMarketDataProvider, YahooFinanceProvider>(client =>
        {
            client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IMarketDataService, MarketDataService>();
        services.AddSingleton<FinnhubTradeFeed>();
        services.AddSingleton<ILiveTradeFeed>(sp => sp.GetRequiredService<FinnhubTradeFeed>());
        services.AddHostedService(sp => sp.GetRequiredService<FinnhubTradeFeed>());
        services.AddScoped<IChartDrawingService, ChartDrawingService>();

        services.Configure<SourcesOptions>(configuration.GetSection(SourcesOptions.SectionName));
        services.AddSingleton<ITickerExtractor, TickerExtractor>();
        services.AddSingleton<ISentimentAnalyzer>(sp => CreateSentimentAnalyzer(sp, finBertDirectory));
        services.AddScoped<ArticleIngestor>();
        services.AddScoped<INewsService, NewsService>();
        services.Configure<ScoringOptions>(configuration.GetSection(ScoringOptions.SectionName));
        services.AddScoped<ISuggestionService, SuggestionService>();
        services.AddScoped<IScoringSettings, ScoringSettings>();
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddScoped<IJournalService, JournalService>();
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));
        services.AddScoped<IBackupService, BackupService>();
        services.AddHostedService<BackupWorker>();
        services.AddHttpClient<ICalendarService, CalendarService>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHostedService<CalendarAlertWorker>();
        services.Configure<SummaryOptions>(configuration.GetSection(SummaryOptions.SectionName));
        services.AddHttpClient<ISummaryNarrator, OllamaNarrator>(client => client.Timeout = TimeSpan.FromMinutes(3));
        services.AddScoped<IDailySummaryService, DailySummaryService>();
        services.AddHostedService<SummaryWorker>();
        services.AddScoped<IChartTemplateService, ChartTemplateService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddHttpClient<IAlertPublisher, NtfyPublisher>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IInstrumentService, InstrumentService>();
        services.AddSingleton<ISourceStatusRegistry, SourceStatusRegistry>();
        services.AddSingleton<ISymbolProvider, SymbolProvider>();
        services.AddSingleton(new IntradayOptions());
        services.AddSingleton<IRadarService, RadarService>();
        services.AddHostedService<RadarWorker>();
        services.AddHostedService<SuggestionWorker>();
        services.AddHostedService<QuoteWarmupWorker>();
        services.AddHostedService<PriceWatchWorker>();

        AddConnector<RssConnector>(services, 20);
        AddConnector<GdeltConnector>(services, 40);
        AddConnector<StockTwitsConnector>(services, 20);
        AddConnector<FinnhubConnector>(services, 20);
        AddConnector<RedditConnector>(services, 20);
        AddConnector<EdgarConnector>(services, 20);
        AddConnector<MarketauxConnector>(services, 20);
        AddConnector<FredConnector>(services, 20);
        AddConnector<HackerNewsConnector>(services, 20);

        services.AddHostedService<IngestionWorker>();
        return services;
    }

    private static void AddConnector<TConnector>(IServiceCollection services, int timeoutSeconds)
        where TConnector : class, ISourceConnector
    {
        services.AddHttpClient<TConnector>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
        services.AddSingleton<ISourceConnector>(sp => sp.GetRequiredService<TConnector>());
    }

    private static ISentimentAnalyzer CreateSentimentAnalyzer(IServiceProvider services, string finBertDirectory)
    {
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Sentiment");

        if (!File.Exists(Path.Combine(finBertDirectory, "model.onnx")) || !File.Exists(Path.Combine(finBertDirectory, "vocab.txt")))
        {
            logger.LogWarning("FinBERT model not found in {Directory}; articles will be stored without sentiment", finBertDirectory);
            return new NullSentimentAnalyzer();
        }

        return new FinBertSentimentAnalyzer(finBertDirectory);
    }
}
