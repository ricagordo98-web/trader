using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Health;
using Trader.Infrastructure.News.Sources;
using Trader.Infrastructure.Summary;
using Trader.Core.Maintenance;

namespace Trader.Web.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/system").RequireAuthorization();

        group.MapGet("/backups", async (IBackupService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        group.MapPost("/backups", async (IBackupService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.CreateAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                return Results.Problem(title: "Não foi possível criar o backup: " + ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapGet("/health", async (
            IMarketDataService marketData,
            ISourceStatusRegistry sources,
            IOptions<SourcesOptions> sourceOptions,
            IOptions<SummaryOptions> summaryOptions,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<HealthItem> items =
            [
                HealthEvaluator.Prices(await marketData.GetQuotesAsync(null, cancellationToken), now),
                HealthEvaluator.News(sources.GetAll()),
                HealthEvaluator.Optional(
                    "Preços ao vivo (Finnhub)",
                    !string.IsNullOrWhiteSpace(sourceOptions.Value.Finnhub.ApiKey),
                    null,
                    "Chave configurada: ações dos EUA e forex em direto.",
                    "Sem chave Finnhub: só há preços do Yahoo (com atraso).")
            ];

            string ollamaModel = summaryOptions.Value.OllamaModel;
            bool? ollamaUp = null;

            if (!string.IsNullOrWhiteSpace(ollamaModel))
            {
                try
                {
                    using HttpClient client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(3);
                    using HttpResponseMessage response = await client.GetAsync(summaryOptions.Value.OllamaUrl.TrimEnd('/') + "/api/tags", cancellationToken);
                    ollamaUp = response.IsSuccessStatusCode;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    ollamaUp = false;
                }
            }

            items.Add(HealthEvaluator.Optional(
                "Explicações com IA (Ollama)",
                !string.IsNullOrWhiteSpace(ollamaModel),
                ollamaUp,
                $"Modelo {ollamaModel} a responder.",
                "Desligado: as explicações usam o texto automático."));

            return Results.Ok(items);
        });

        return app;
    }
}
