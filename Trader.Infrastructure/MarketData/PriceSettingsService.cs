using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.MarketData;

public sealed class PriceSettingsService(TraderDbContext dbContext, IMemoryCache cache) : IPriceSettings
{
    public const string Key = "price.cryptoBasis";

    private const string CacheKey = "price:cryptoBasis";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<string> GetCryptoBasisAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        string? stored = await dbContext.AppSettings.AsNoTracking().Where(s => s.Key == Key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        string basis = stored is not null && CryptoBasis.IsValid(stored) ? stored : CryptoBasis.Usd;
        cache.Set(CacheKey, basis, CacheDuration);
        return basis;
    }

    public async Task<string?> SetCryptoBasisAsync(string basis, CancellationToken cancellationToken)
    {
        string normalized = basis.Trim().ToLowerInvariant();

        if (!CryptoBasis.IsValid(normalized))
        {
            return "Fonte de preço inválida.";
        }

        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == Key, cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = Key, Value = normalized });
        }
        else
        {
            setting.Value = normalized;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);
        return null;
    }
}
