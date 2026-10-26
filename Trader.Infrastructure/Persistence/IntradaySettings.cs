using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Intraday;
using Trader.Core.Models;

namespace Trader.Infrastructure.Persistence;

public sealed class IntradaySettings(TraderDbContext dbContext, IntradayOptions live) : IIntradaySettings
{
    private const string Key = "intraday-options";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PropertyInfo[] Properties = typeof(IntradayOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).ToArray();

    public async Task<IntradayOptions> GetAsync(CancellationToken cancellationToken)
    {
        IntradayOptions result = new();
        Copy(live, result);
        return await Task.FromResult(result);
    }

    public async Task<IReadOnlyList<string>> SaveAsync(IntradayOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = IntradayValidator.Validate(options);

        if (errors.Count > 0)
        {
            return errors;
        }

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
        Copy(options, live);
        return [];
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await dbContext.AppSettings.Where(s => s.Key == Key).ExecuteDeleteAsync(cancellationToken);
        Copy(new IntradayOptions(), live);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        string? json = await dbContext.AppSettings.AsNoTracking().Where(s => s.Key == Key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);

        if (json is null)
        {
            return;
        }

        try
        {
            if (JsonSerializer.Deserialize<IntradayOptions>(json, JsonOptions) is { } stored)
            {
                Copy(stored, live);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void Copy(IntradayOptions from, IntradayOptions to)
    {
        foreach (PropertyInfo property in Properties)
        {
            property.SetValue(to, property.GetValue(from));
        }
    }
}
