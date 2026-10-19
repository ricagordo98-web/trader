using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.News;

public sealed class SourceStatusRegistry : ISourceStatusRegistry
{
    private readonly ConcurrentDictionary<string, SourceStatus> _statuses = new();

    public void Report(SourceStatus status) => _statuses[status.Name] = status;

    public IReadOnlyList<SourceStatus> GetAll() => _statuses.Values.OrderBy(s => !s.Enabled).ThenBy(s => s.Name).ToList();
}

public sealed class SymbolProvider(IServiceScopeFactory scopeFactory) : ISymbolProvider
{
    public async Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        TraderDbContext dbContext = scope.ServiceProvider.GetRequiredService<TraderDbContext>();

        return await dbContext.Instruments.AsNoTracking().OrderBy(i => i.SortOrder).Select(i => i.Symbol).ToListAsync(cancellationToken);
    }
}
