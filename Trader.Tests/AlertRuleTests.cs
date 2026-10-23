using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Infrastructure.Alerts;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

public class AlertRuleEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    private static RadarRow Row(string symbol = "AAPL", double? change = 3.5, double? rvol = 4, AssetClass assetClass = AssetClass.Stock) =>
        new(symbol, symbol, assetClass, 100, change, null, rvol, null, Now);

    private static RadarEvent Event(RadarEventKind kind, TradeDirection direction = TradeDirection.Buy) =>
        new("AAPL", "AAPL", kind, direction, 0.7, "rompeu o máximo", 100, Now);

    private static AlertRule Rule(Action<AlertRule>? configure = null)
    {
        AlertRule rule = new() { Id = 1, UserId = "u", Name = "Teste", MinRelativeVolume = 3 };
        configure?.Invoke(rule);
        return rule;
    }

    [Fact]
    public void Evaluate_VolumeRuleMatchesWhenTheThresholdIsReached()
    {
        RuleMatch match = AlertRuleEvaluator.Evaluate(Rule(), Row(rvol: 4), [], null);

        match.Matched.Should().BeTrue();
        match.Reason.Should().Contain("volume 4.0×");
        AlertRuleEvaluator.Evaluate(Rule(), Row(rvol: 2.5), [], null).Matched.Should().BeFalse();
        AlertRuleEvaluator.Evaluate(Rule(), Row(rvol: null), [], null).Matched.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_AllConditionsMustHold()
    {
        AlertRule rule = Rule(r =>
        {
            r.MinChangePercent = 3;
            r.Direction = RuleDirection.Up;
            r.Sentiment = RuleSentiment.Positive;
        });

        AlertRuleEvaluator.Evaluate(rule, Row(change: 3.5, rvol: 4), [], 0.6).Matched.Should().BeTrue();
        AlertRuleEvaluator.Evaluate(rule, Row(change: 3.5, rvol: 4), [], 0.1).Matched.Should().BeFalse();
        AlertRuleEvaluator.Evaluate(rule, Row(change: 3.5, rvol: 4), [], null).Matched.Should().BeFalse();
        AlertRuleEvaluator.Evaluate(rule, Row(change: -3.5, rvol: 4), [], 0.6).Matched.Should().BeFalse();
        AlertRuleEvaluator.Evaluate(rule, Row(change: 2, rvol: 4), [], 0.6).Matched.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ScopeBySymbolAndAssetClass()
    {
        AlertRuleEvaluator.Evaluate(Rule(r => r.Symbol = "TSLA"), Row("AAPL"), [], null).Matched.Should().BeFalse();
        AlertRuleEvaluator.Evaluate(Rule(r => r.Symbol = "aapl"), Row("AAPL"), [], null).Matched.Should().BeTrue();
        AlertRuleEvaluator.Evaluate(Rule(r => r.AssetClass = AssetClass.Crypto), Row(), [], null).Matched.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EventRule_RequiresTheEventInTheRightDirection()
    {
        AlertRule rule = Rule(r =>
        {
            r.MinRelativeVolume = null;
            r.EventKind = RadarEventKind.OpeningRangeBreakout;
            r.Direction = RuleDirection.Up;
        });

        AlertRuleEvaluator.Evaluate(rule, Row(), [Event(RadarEventKind.OpeningRangeBreakout)], null).Matched.Should().BeTrue();
        AlertRuleEvaluator.Evaluate(rule, Row(), [Event(RadarEventKind.OpeningRangeBreakout, TradeDirection.Sell)], null).Matched.Should().BeFalse();
        AlertRuleEvaluator.Evaluate(rule, Row(), [Event(RadarEventKind.Gap)], null).Matched.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_DisabledRule_NeverMatches() =>
        AlertRuleEvaluator.Evaluate(Rule(r => r.Enabled = false), Row(), [], null).Matched.Should().BeFalse();

    [Theory]
    [InlineData(23, 22, 7, true)]
    [InlineData(3, 22, 7, true)]
    [InlineData(12, 22, 7, false)]
    [InlineData(9, 8, 18, true)]
    [InlineData(18, 8, 18, false)]
    [InlineData(5, null, null, false)]
    [InlineData(5, 6, 6, false)]
    public void InQuietHours_HandlesRangesThatCrossMidnight(int hour, int? from, int? to, bool expected) =>
        AlertRuleEvaluator.InQuietHours(hour, from, to).Should().Be(expected);

    [Fact]
    public void Validate_RequiresNameAndAtLeastOneCondition()
    {
        AlertRuleDto empty = new(0, "", true, null, null, null, null, RuleDirection.Any, null, RuleSentiment.Any, 60);
        AlertRuleDto valid = empty with { Name = "Regra", MinChangePercent = 2 };

        AlertRuleEvaluator.Validate(empty).Should().HaveCount(2);
        AlertRuleEvaluator.Validate(valid).Should().BeEmpty();
        AlertRuleEvaluator.Validate(valid with { CooldownMinutes = 1 }).Should().NotBeEmpty();
        AlertRuleEvaluator.Validate(valid with { MinRelativeVolume = 100 }).Should().NotBeEmpty();
    }
}

public class AlertRuleServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IAlertService _alerts = Substitute.For<IAlertService>();
    private readonly IUserPreferences _preferences = Substitute.For<IUserPreferences>();
    private TraderDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new TraderDbContext(new DbContextOptionsBuilder<TraderDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
        _preferences.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UserPreferences.Default);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private AlertRuleService Service() => new(_dbContext, _alerts, _preferences);

    private static AlertRuleDto Dto(string name = "Volume") => new(0, name, true, null, null, 3, null, RuleDirection.Any, null, RuleSentiment.Any, 60);

    private static RadarRow Row(string symbol) => new(symbol, symbol, AssetClass.Stock, 100, 2, null, 5, null, Now);

    [Fact]
    public async Task SaveListDelete_AreScopedPerUser()
    {
        AlertRuleService service = Service();

        (AlertRuleDto? saved, IReadOnlyList<string> errors) = await service.SaveAsync("u1", Dto(), CancellationToken.None);
        await service.SaveAsync("u2", Dto("Outra"), CancellationToken.None);

        errors.Should().BeEmpty();
        saved!.Id.Should().BeGreaterThan(0);
        (await service.ListAsync("u1", CancellationToken.None)).Should().ContainSingle().Which.Name.Should().Be("Volume");
        (await service.DeleteAsync("u2", saved.Id, CancellationToken.None)).Should().BeFalse();
        (await service.DeleteAsync("u1", saved.Id, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Save_UpdatesAnExistingRule()
    {
        AlertRuleService service = Service();
        AlertRuleDto saved = (await service.SaveAsync("u1", Dto(), CancellationToken.None)).Rule!;

        await service.SaveAsync("u1", saved with { Enabled = false, Name = "Renomeada" }, CancellationToken.None);

        AlertRuleDto loaded = (await service.ListAsync("u1", CancellationToken.None)).Single();
        loaded.Enabled.Should().BeFalse();
        loaded.Name.Should().Be("Renomeada");
    }

    [Fact]
    public async Task Evaluate_RaisesAUserAlertAndRespectsTheCooldown()
    {
        AlertRuleService service = Service();
        await service.SaveAsync("u1", Dto(), CancellationToken.None);
        List<RadarRow> board = [Row("COOLDOWNA")];

        int first = await service.EvaluateAsync(board, [], Now, CancellationToken.None);
        int second = await service.EvaluateAsync(board, [], Now.AddMinutes(10), CancellationToken.None);
        int third = await service.EvaluateAsync(board, [], Now.AddMinutes(61), CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(0);
        third.Should().Be(1);
        await _alerts.Received(2).RaiseAsync(
            Arg.Is<IReadOnlyList<Alert>>(a => a.Count == 1 && a[0].UserId == "u1" && a[0].Kind == AlertKind.Rule && a[0].Symbol == "COOLDOWNA"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_HidesAssetClassesTheUserDisabled()
    {
        _preferences.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(UserPreferences.Default with { EnabledClasses = [AssetClass.Crypto] });
        AlertRuleService service = Service();
        await service.SaveAsync("u1", Dto(), CancellationToken.None);

        (await service.EvaluateAsync([Row("HIDDENA")], [], Now, CancellationToken.None)).Should().Be(0);
    }
}
