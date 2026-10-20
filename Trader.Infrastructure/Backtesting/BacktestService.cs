using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Backtesting;
using Trader.Core.Models;
using Trader.Core.Scoring;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Backtesting;

public sealed class BacktestService(
    TraderDbContext dbContext,
    IMarketDataService marketData,
    IScoringSettings scoringSettings,
    ILogger<BacktestService> logger) : IBacktestService
{
    private const string SettingKey = "backtest.last";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = BacktestRequestValidator.Validate(request);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        if (!await Gate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Já existe um backtest em execução. Aguarda que termine.");
        }

        try
        {
            List<Instrument> instruments = await dbContext.Instruments.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
            ScoringOptions scoring = await scoringSettings.GetAsync(cancellationToken);
            List<SymbolHistory> histories = await LoadHistoriesAsync(instruments, request.Range, cancellationToken);

            BacktestResult result = await Task.Run(
                () => BacktestRunner.Run(histories, scoring, request, DateTimeOffset.UtcNow),
                cancellationToken);

            await SaveAsync(result, cancellationToken);
            logger.LogInformation(
                "Backtest {Range}: {Trades} trades over {Symbols} symbols, net {Average:0.000}R average",
                request.Range,
                result.Strategy.Trades,
                result.SymbolsTested,
                result.Strategy.AverageNetR);

            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<BacktestResult?> GetLastAsync(CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);

        return setting is null ? null : JsonSerializer.Deserialize<BacktestResult>(setting.Value, JsonOptions);
    }

    private async Task<List<SymbolHistory>> LoadHistoriesAsync(IReadOnlyList<Instrument> instruments, string range, CancellationToken cancellationToken)
    {
        using SemaphoreSlim throttle = new(4);

        Task<SymbolHistory?>[] tasks = instruments
            .Select(async instrument =>
            {
                await throttle.WaitAsync(cancellationToken);

                try
                {
                    IReadOnlyList<Candle> candles = await marketData.GetCandlesAsync(instrument.Symbol, "1d", range, cancellationToken);
                    return new SymbolHistory(instrument.Symbol, instrument.Name, instrument.AssetClass, candles);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning("Backtest skipped {Symbol}: {Message}", instrument.Symbol, ex.Message);
                    return null;
                }
                finally
                {
                    throttle.Release();
                }
            })
            .ToArray();

        return (await Task.WhenAll(tasks)).OfType<SymbolHistory>().ToList();
    }

    private async Task SaveAsync(BacktestResult result, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(result, JsonOptions);
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
}
