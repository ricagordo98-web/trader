using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Backtesting;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Backtesting;

public sealed class IntradayBacktestService(
    TraderDbContext dbContext,
    IMarketDataService marketData,
    IntradayOptions options,
    ILogger<IntradayBacktestService> logger) : IIntradayBacktestService
{
    private const string SettingKey = "backtest.intraday.last";
    private const int MaxParallelFetches = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IntradayBacktestResult> RunAsync(IntradayBacktestRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = IntradayBacktestRunner.Validate(request);

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
            IQueryable<Instrument> query = dbContext.Instruments.AsNoTracking();

            if (request.AssetClass is { } assetClass)
            {
                query = query.Where(i => i.AssetClass == assetClass);
            }

            List<Instrument> instruments = await query.OrderBy(i => i.SortOrder).Take(request.MaxSymbols).ToListAsync(cancellationToken);
            List<IntradayHistory> histories = await LoadAsync(instruments, cancellationToken);

            IntradayBacktestResult result = await Task.Run(
                () => IntradayBacktestRunner.Run(histories, options, request, DateTimeOffset.UtcNow),
                cancellationToken);

            await SaveAsync(result, cancellationToken);
            logger.LogInformation("Intraday backtest: {Trades} trades over {Symbols} symbols", result.Strategy.Trades, result.SymbolsTested);
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IntradayBacktestResult?> GetLastAsync(CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);
        return setting is null ? null : JsonSerializer.Deserialize<IntradayBacktestResult>(setting.Value, JsonOptions);
    }

    private async Task<List<IntradayHistory>> LoadAsync(IReadOnlyList<Instrument> instruments, CancellationToken cancellationToken)
    {
        using SemaphoreSlim throttle = new(MaxParallelFetches);

        IntradayHistory?[] results = await Task.WhenAll(instruments.Select(async instrument =>
        {
            await throttle.WaitAsync(cancellationToken);

            try
            {
                IReadOnlyList<Candle> candles = await marketData.GetCandlesAsync(instrument.Symbol, "5m", "1mo", cancellationToken);
                return new IntradayHistory(instrument.Symbol, instrument.Name, instrument.AssetClass, candles);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Intraday backtest skipped {Symbol}: {Message}", instrument.Symbol, ex.Message);
                return null;
            }
            finally
            {
                throttle.Release();
            }
        }));

        return results.OfType<IntradayHistory>().ToList();
    }

    private async Task SaveAsync(IntradayBacktestResult result, CancellationToken cancellationToken)
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
