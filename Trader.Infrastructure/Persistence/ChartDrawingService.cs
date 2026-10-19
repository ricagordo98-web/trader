using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.Persistence;

public sealed class ChartDrawingService(TraderDbContext dbContext) : IChartDrawingService
{
    public async Task<string> GetAsync(string symbol, CancellationToken cancellationToken)
    {
        string? json = await dbContext.ChartDrawings
            .AsNoTracking()
            .Where(d => d.Symbol == symbol)
            .Select(d => d.Json)
            .FirstOrDefaultAsync(cancellationToken);

        return json ?? "[]";
    }

    public async Task SaveAsync(string symbol, string json, CancellationToken cancellationToken)
    {
        ChartDrawing? drawing = await dbContext.ChartDrawings.FirstOrDefaultAsync(d => d.Symbol == symbol, cancellationToken);

        if (drawing is null)
        {
            drawing = new ChartDrawing { Symbol = symbol };
            dbContext.ChartDrawings.Add(drawing);
        }

        drawing.Json = json;
        drawing.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
