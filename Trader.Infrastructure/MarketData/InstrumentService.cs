using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.MarketData;

public sealed partial class InstrumentService(TraderDbContext dbContext, IMarketDataProvider provider) : IInstrumentService
{
    public async Task<IReadOnlyList<Instrument>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Instruments.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);

    public async Task<AddInstrumentResult> AddAsync(AddInstrumentRequest request, CancellationToken cancellationToken)
    {
        string symbol = request.Symbol.Trim().ToUpperInvariant();
        string name = request.Name.Trim();
        string currency = request.Currency.Trim().ToUpperInvariant();

        if (!SymbolPattern().IsMatch(symbol))
        {
            return new AddInstrumentResult(null, "Símbolo inválido. Usa o símbolo do Yahoo Finance (ex.: AAPL, GC=F, EURUSD=X).");
        }

        if (name.Length is 0 or > 128 || currency.Length is < 3 or > 8)
        {
            return new AddInstrumentResult(null, "Indica um nome (até 128 caracteres) e uma moeda (ex.: USD).");
        }

        if (await dbContext.Instruments.AnyAsync(i => i.Symbol == symbol, cancellationToken))
        {
            return new AddInstrumentResult(null, "Este instrumento já está na watchlist.");
        }

        try
        {
            ChartSeries series = await provider.GetChartAsync(symbol, "1d", "5d", cancellationToken);

            if (series.Candles.Count == 0)
            {
                return new AddInstrumentResult(null, "Símbolo não encontrado no Yahoo Finance.");
            }
        }
        catch (HttpRequestException)
        {
            return new AddInstrumentResult(null, "Símbolo não encontrado no Yahoo Finance.");
        }

        int nextOrder = await dbContext.Instruments.Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? -1;
        Instrument instrument = new()
        {
            Symbol = symbol,
            Name = name,
            AssetClass = request.AssetClass,
            Currency = currency,
            SortOrder = nextOrder + 1
        };

        dbContext.Instruments.Add(instrument);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new AddInstrumentResult(instrument, null);
    }

    public async Task<bool> RemoveAsync(string symbol, CancellationToken cancellationToken) =>
        await dbContext.Instruments.Where(i => i.Symbol == symbol).ExecuteDeleteAsync(cancellationToken) > 0;

    [GeneratedRegex(@"^[A-Z0-9.=\-^]{1,20}$")]
    private static partial Regex SymbolPattern();
}
