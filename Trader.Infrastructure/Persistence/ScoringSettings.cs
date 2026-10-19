using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Infrastructure.Persistence;

public sealed class ScoringSettings(TraderDbContext dbContext, IOptions<ScoringOptions> defaults) : IScoringSettings
{
    private const string Key = "scoring";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ScoringOptions> GetAsync(CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, cancellationToken);

        if (setting is null)
        {
            return Clone(defaults.Value);
        }

        return JsonSerializer.Deserialize<ScoringOptions>(setting.Value, JsonOptions) ?? Clone(defaults.Value);
    }

    public async Task SaveAsync(ScoringOptions options, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(options, JsonOptions);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == Key, cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = Key, Value = json });
        }
        else
        {
            setting.Value = json;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await dbContext.AppSettings.Where(s => s.Key == Key).ExecuteDeleteAsync(cancellationToken);
    }

    private static ScoringOptions Clone(ScoringOptions options) =>
        JsonSerializer.Deserialize<ScoringOptions>(JsonSerializer.Serialize(options, JsonOptions), JsonOptions)!;
}
