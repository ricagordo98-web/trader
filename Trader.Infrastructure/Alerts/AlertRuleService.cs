using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Alerts;

public sealed class AlertRuleService(TraderDbContext dbContext, IAlertService alerts, IUserPreferences preferences) : IAlertRuleService
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastTriggered = new();
    private static readonly TimeSpan NewsWindow = TimeSpan.FromHours(3);

    public async Task<IReadOnlyList<AlertRuleDto>> ListAsync(string userId, CancellationToken cancellationToken) =>
        (await dbContext.AlertRules.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.Id).ToListAsync(cancellationToken))
            .Select(AlertRuleEvaluator.ToDto)
            .ToList();

    public async Task<(AlertRuleDto? Rule, IReadOnlyList<string> Errors)> SaveAsync(string userId, AlertRuleDto rule, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = AlertRuleEvaluator.Validate(rule);

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        AlertRule? entity = rule.Id > 0 ? await dbContext.AlertRules.FirstOrDefaultAsync(r => r.Id == rule.Id && r.UserId == userId, cancellationToken) : null;

        if (entity is null)
        {
            if (await dbContext.AlertRules.CountAsync(r => r.UserId == userId, cancellationToken) >= AlertRuleEvaluator.MaxRules)
            {
                return (null, [$"Máximo de {AlertRuleEvaluator.MaxRules} regras."]);
            }

            entity = new AlertRule { UserId = userId, CreatedAt = DateTimeOffset.UtcNow };
            dbContext.AlertRules.Add(entity);
        }

        entity.Name = rule.Name.Trim();
        entity.Enabled = rule.Enabled;
        entity.AssetClass = rule.AssetClass;
        entity.Symbol = string.IsNullOrWhiteSpace(rule.Symbol) ? null : rule.Symbol.Trim().ToUpperInvariant();
        entity.MinRelativeVolume = rule.MinRelativeVolume;
        entity.MinChangePercent = rule.MinChangePercent;
        entity.Direction = rule.Direction;
        entity.EventKind = rule.EventKind;
        entity.Sentiment = rule.Sentiment;
        entity.CooldownMinutes = rule.CooldownMinutes;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (AlertRuleEvaluator.ToDto(entity), []);
    }

    public async Task<bool> DeleteAsync(string userId, long id, CancellationToken cancellationToken) =>
        await dbContext.AlertRules.Where(r => r.UserId == userId && r.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<int> EvaluateAsync(IReadOnlyList<RadarRow> board, IReadOnlyList<RadarEvent> events, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<AlertRule> rules = await dbContext.AlertRules.AsNoTracking().Where(r => r.Enabled).ToListAsync(cancellationToken);

        if (rules.Count == 0 || board.Count == 0)
        {
            return 0;
        }

        Dictionary<string, UserPreferences> users = [];
        Dictionary<string, double?> sentiment = [];
        List<Alert> raised = [];

        foreach (AlertRule rule in rules)
        {
            if (!users.TryGetValue(rule.UserId, out UserPreferences? user))
            {
                user = await preferences.GetAsync(rule.UserId, cancellationToken);
                users[rule.UserId] = user;
            }

            foreach (RadarRow row in board.Where(r => user.Allows(r.AssetClass)))
            {
                string key = $"{rule.Id}|{row.Symbol}";

                if (LastTriggered.TryGetValue(key, out DateTimeOffset last) && now - last < TimeSpan.FromMinutes(rule.CooldownMinutes))
                {
                    continue;
                }

                double? news = null;

                if (rule.Sentiment != RuleSentiment.Any && !sentiment.TryGetValue(row.Symbol, out news))
                {
                    news = await RecentSentimentAsync(row.Symbol, now, cancellationToken);
                    sentiment[row.Symbol] = news;
                }

                RuleMatch match = AlertRuleEvaluator.Evaluate(rule, row, events.Where(e => e.Symbol == row.Symbol).ToList(), news);

                if (!match.Matched)
                {
                    continue;
                }

                LastTriggered[key] = now;

                raised.Add(new Alert
                {
                    UserId = rule.UserId,
                    Kind = AlertKind.Rule,
                    Symbol = row.Symbol,
                    Title = $"{rule.Name}: {row.Symbol}",
                    Message = match.Reason,
                    CreatedAt = now,
                    Quiet = AlertRuleEvaluator.InQuietHours(DateTime.Now.Hour, user.QuietFromHour, user.QuietToHour)
                });
            }
        }

        if (raised.Count > 0)
        {
            await alerts.RaiseAsync(raised, cancellationToken);
        }

        return raised.Count;
    }

    private async Task<double?> RecentSentimentAsync(string symbol, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset since = now - NewsWindow;

        List<double> values = await (
            from link in dbContext.Set<ArticleSymbol>().AsNoTracking()
            join article in dbContext.Articles.AsNoTracking() on link.ArticleId equals article.Id
            where link.Symbol == symbol && article.PublishedAt >= since && article.Sentiment != null
            select article.Sentiment!.Value)
            .ToListAsync(cancellationToken);

        return values.Count == 0 ? null : values.Average();
    }
}
