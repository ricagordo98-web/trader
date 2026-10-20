using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Charts;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Charts;

public sealed class ChartTemplateService(TraderDbContext dbContext) : IChartTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ChartTemplate>> GetAsync(string userId, CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key(userId), cancellationToken);

        return setting is null ? [] : JsonSerializer.Deserialize<List<ChartTemplate>>(setting.Value, JsonOptions) ?? [];
    }

    public async Task<IReadOnlyList<string>> SaveAsync(string userId, IReadOnlyList<ChartTemplate> templates, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = ChartTemplateValidator.Validate(templates);

        if (errors.Count > 0)
        {
            return errors;
        }

        string json = JsonSerializer.Serialize(templates.Select(t => t with { Name = t.Name.Trim() }), JsonOptions);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == Key(userId), cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = Key(userId), Value = json });
        }
        else
        {
            setting.Value = json;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return [];
    }

    private static string Key(string userId) => $"chart.templates:{userId}";
}
