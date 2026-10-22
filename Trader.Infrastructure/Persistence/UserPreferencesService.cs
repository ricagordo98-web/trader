using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Models;

namespace Trader.Infrastructure.Persistence;

public sealed class UserPreferencesService(TraderDbContext dbContext) : IUserPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UserPreferences> GetAsync(string userId, CancellationToken cancellationToken)
    {
        string? json = await dbContext.AppSettings.AsNoTracking().Where(s => s.Key == KeyFor(userId)).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);

        if (json is null)
        {
            return UserPreferences.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions) ?? UserPreferences.Default;
        }
        catch (JsonException)
        {
            return UserPreferences.Default;
        }
    }

    public async Task<IReadOnlyList<string>> SaveAsync(string userId, UserPreferences preferences, CancellationToken cancellationToken)
    {
        UserPreferences normalized = preferences with
        {
            DisplayCurrency = preferences.DisplayCurrency?.Trim().ToUpperInvariant(),
            EnabledClasses = preferences.EnabledClasses.Distinct().ToList()
        };

        IReadOnlyList<string> errors = UserPreferencesValidator.Validate(normalized);

        if (errors.Count > 0)
        {
            return errors;
        }

        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == KeyFor(userId), cancellationToken);

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = KeyFor(userId), Value = json });
        }
        else
        {
            setting.Value = json;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return [];
    }

    private static string KeyFor(string userId) => $"prefs:{userId}";
}
