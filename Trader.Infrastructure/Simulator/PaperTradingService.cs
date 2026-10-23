using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Core.Simulator;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Simulator;

public sealed class PaperTradingService(TraderDbContext dbContext, IMarketDataService marketData, ILogger<PaperTradingService> logger) : IPaperTradingService
{
    private const int MaxOpenTrades = 30;
    private const int MaxClosedShown = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PaperOverview> GetAsync(string userId, CancellationToken cancellationToken)
    {
        PaperAccount account = await LoadAccountAsync(userId, cancellationToken);
        List<PaperTrade> open = await dbContext.PaperTrades.Where(t => t.UserId == userId && t.Status == PaperStatus.Open).ToListAsync(cancellationToken);
        Dictionary<string, double?> prices = [];

        foreach (PaperTrade trade in open)
        {
            await ResolveAsync(trade, prices, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        List<PaperTrade> stillOpen = open.Where(t => t.Status == PaperStatus.Open).ToList();
        List<PaperTrade> closed = await dbContext.PaperTrades
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Status != PaperStatus.Open)
            .OrderByDescending(t => t.ClosedAt)
            .Take(MaxClosedShown)
            .ToListAsync(cancellationToken);

        List<PaperTradeDto> openDtos = stillOpen.Select(t => ToDto(t, prices.GetValueOrDefault(t.Symbol))).ToList();
        List<PaperTradeDto> closedDtos = closed.Select(t => ToDto(t, null)).ToList();

        return new PaperOverview(account, PaperCalculator.Summarize(account, openDtos, closedDtos), openDtos, closedDtos);
    }

    public async Task<(PaperTradeDto? Trade, IReadOnlyList<string> Errors)> OpenAsync(string userId, OpenPaperTradeRequest request, CancellationToken cancellationToken)
    {
        Instrument? instrument = await dbContext.Instruments.AsNoTracking().FirstOrDefaultAsync(i => i.Symbol == request.Symbol, cancellationToken);

        if (instrument is null)
        {
            return (null, ["Ativo desconhecido."]);
        }

        if (await dbContext.PaperTrades.CountAsync(t => t.UserId == userId && t.Status == PaperStatus.Open, cancellationToken) >= MaxOpenTrades)
        {
            return (null, [$"Máximo de {MaxOpenTrades} posições abertas."]);
        }

        double entry = request.Entry is > 0 ? request.Entry.Value : await marketData.GetLatestPriceAsync(instrument.Symbol, cancellationToken) ?? 0;
        IReadOnlyList<string> errors = PaperCalculator.Validate(request, entry);

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        PaperAccount account = await LoadAccountAsync(userId, cancellationToken);
        double quantity = PaperCalculator.Size(account.Capital, account.RiskPercent, entry, request.StopLoss);

        if (quantity <= 0)
        {
            return (null, ["Não foi possível calcular a quantidade. Verifica o capital, o risco e o stop loss."]);
        }

        PaperTrade trade = new()
        {
            UserId = userId,
            Symbol = instrument.Symbol,
            Name = instrument.Name,
            Direction = request.Direction,
            Quantity = quantity,
            EntryPrice = entry,
            StopLoss = request.StopLoss,
            TakeProfit = request.TakeProfit,
            OpenedAt = DateTimeOffset.UtcNow,
            Status = PaperStatus.Open,
            SuggestionId = request.SuggestionId
        };

        dbContext.PaperTrades.Add(trade);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (ToDto(trade, entry), []);
    }

    public async Task<bool> CloseAsync(string userId, long id, CancellationToken cancellationToken)
    {
        PaperTrade? trade = await dbContext.PaperTrades.FirstOrDefaultAsync(t => t.UserId == userId && t.Id == id && t.Status == PaperStatus.Open, cancellationToken);

        if (trade is null)
        {
            return false;
        }

        double? price = await marketData.GetLatestPriceAsync(trade.Symbol, cancellationToken);

        if (price is null)
        {
            return false;
        }

        trade.Status = PaperStatus.ClosedManually;
        trade.ExitPrice = price;
        trade.ClosedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<string>> SaveAccountAsync(string userId, PaperAccount account, CancellationToken cancellationToken)
    {
        if (account.Capital is < 100 or > 100_000_000 || account.RiskPercent is < 0.1 or > 10)
        {
            return ["O capital tem de estar entre 100 e 100 000 000 e o risco entre 0,1% e 10%."];
        }

        string json = JsonSerializer.Serialize(account, JsonOptions);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == AccountKey(userId), cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = AccountKey(userId), Value = json });
        }
        else
        {
            setting.Value = json;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return [];
    }

    public async Task ResetAsync(string userId, CancellationToken cancellationToken) =>
        await dbContext.PaperTrades.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    private async Task ResolveAsync(PaperTrade trade, Dictionary<string, double?> prices, CancellationToken cancellationToken)
    {
        try
        {
            bool recent = DateTimeOffset.UtcNow - trade.OpenedAt < TimeSpan.FromDays(4);
            IReadOnlyList<Candle> candles = recent
                ? await marketData.GetCandlesAsync(trade.Symbol, "5m", "5d", cancellationToken)
                : await marketData.GetCandlesAsync(trade.Symbol, "60m", "3mo", cancellationToken);

            if (PaperCalculator.Resolve(trade, candles) is { } outcome)
            {
                trade.Status = outcome.Status;
                trade.ExitPrice = outcome.ExitPrice;
                trade.ClosedAt = outcome.ClosedAt;
                return;
            }

            if (!prices.ContainsKey(trade.Symbol))
            {
                prices[trade.Symbol] = await marketData.GetLatestPriceAsync(trade.Symbol, cancellationToken);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Could not evaluate simulated trade {Id} ({Symbol}): {Message}", trade.Id, trade.Symbol, ex.Message);
        }
    }

    private async Task<PaperAccount> LoadAccountAsync(string userId, CancellationToken cancellationToken)
    {
        string? json = await dbContext.AppSettings.AsNoTracking().Where(s => s.Key == AccountKey(userId)).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);

        try
        {
            return json is null ? PaperAccount.Default : JsonSerializer.Deserialize<PaperAccount>(json, JsonOptions) ?? PaperAccount.Default;
        }
        catch (JsonException)
        {
            return PaperAccount.Default;
        }
    }

    private static string AccountKey(string userId) => $"paper:{userId}";

    private static PaperTradeDto ToDto(PaperTrade trade, double? currentPrice)
    {
        double? mark = trade.ExitPrice ?? currentPrice;

        return new PaperTradeDto(
            trade.Id,
            trade.Symbol,
            trade.Name,
            trade.Direction,
            trade.Quantity,
            trade.EntryPrice,
            trade.StopLoss,
            trade.TakeProfit,
            trade.OpenedAt,
            trade.Status,
            trade.ClosedAt,
            trade.ExitPrice,
            currentPrice,
            mark is { } price ? PaperCalculator.Pnl(trade, price) : 0,
            mark is { } value ? PaperCalculator.RMultiple(trade, value) : 0);
    }
}
