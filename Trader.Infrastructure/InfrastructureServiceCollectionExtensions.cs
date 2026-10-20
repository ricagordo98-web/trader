using Trader.Infrastructure.Alerts;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Scoring;
using Trader.Infrastructure.Backtesting;
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
        services.AddScoped<IAlertService, AlertService>();
        services.AddHttpClient<IAlertPublisher, NtfyPublisher>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IInstrumentService, InstrumentService>();
        services.AddSingleton<ISourceStatusRegistry, SourceStatusRegistry>();
        services.AddSingleton<ISymbolProvider, SymbolProvider>();
        services.AddHostedService<SuggestionWorker>();

        AddConnector<RssConnector>(services, 20);
        AddConnector<GdeltConnector>(services, 40);
        AddConnector<StockTwitsConnector>(services, 20);
        AddConnector<FinnhubConnector>(services, 20);
        AddConnector<RedditConnector>(services, 20);
        AddConnector<EdgarConnector>(services, 20);
        AddConnector<AlphaVantageConnector>(services, 30);
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
