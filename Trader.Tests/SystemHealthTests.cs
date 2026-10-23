using FluentAssertions;
using Trader.Core.Alerts;
using Trader.Core.Health;
using Trader.Core.Models;

namespace Trader.Tests;

public class SystemHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    private static InstrumentQuote Quote(double? price, DateTimeOffset? asOf) =>
        new("AAA", "AAA", AssetClass.Stock, "USD", price, 0, 0, [], AsOf: asOf);

    [Theory]
    [InlineData(30, "há segundos")]
    [InlineData(300, "há 5 min")]
    [InlineData(7_200, "há 2 h")]
    [InlineData(172_800, "há 2 d")]
    public void Describe_FormatsAge(int seconds, string expected) =>
        DataFreshness.Describe(Now.AddSeconds(-seconds), Now).Should().Be(expected);

    [Fact]
    public void Describe_WithoutTime_SaysSo() =>
        DataFreshness.Describe(null, Now).Should().Be("sem hora da cotação");

    [Fact]
    public void Prices_WithRecentQuote_IsOk() =>
        HealthEvaluator.Prices([Quote(10, Now.AddMinutes(-2))], Now).Level.Should().Be(HealthLevel.Ok);

    [Fact]
    public void Prices_WithOldQuotes_WarnsAboutClosedMarkets() =>
        HealthEvaluator.Prices([Quote(10, Now.AddHours(-5))], Now).Level.Should().Be(HealthLevel.Warning);

    [Fact]
    public void Prices_WithoutAnyPrice_IsDown() =>
        HealthEvaluator.Prices([Quote(null, null)], Now).Level.Should().Be(HealthLevel.Down);

    [Fact]
    public void News_ReportsFailingSources()
    {
        SourceStatus ok = new("A", true, null, 10, Now, 5, 1, null);
        SourceStatus bad = new("B", true, null, 10, Now, 0, 0, "timeout");

        HealthItem item = HealthEvaluator.News([ok, bad]);

        item.Level.Should().Be(HealthLevel.Warning);
        item.Detail.Should().Contain("B");
    }

    [Fact]
    public void Optional_DistinguishesOffFromDown()
    {
        HealthEvaluator.Optional("x", false, null, "on", "off").Level.Should().Be(HealthLevel.Off);
        HealthEvaluator.Optional("x", true, false, "on", "off").Level.Should().Be(HealthLevel.Down);
        HealthEvaluator.Optional("x", true, null, "on", "off").Level.Should().Be(HealthLevel.Ok);
    }
}

public class NtfyRoutingTests
{
    private static AlertDto Alert(AlertKind kind) => new(1, kind, "AAA", "t", "m", 5, DateTimeOffset.UtcNow, true, 3);

    [Fact]
    public void ResolveTopic_PrefersTheUsersOwnTopic()
    {
        NtfySettings global = new(NtfySettings.DefaultServer, "shared-topic-1");

        NtfyRouting.ResolveTopic(global, UserPreferences.Default with { NtfyTopic = "my-own-topic" }).Should().Be("my-own-topic");
        NtfyRouting.ResolveTopic(global, UserPreferences.Default).Should().Be("shared-topic-1");
        NtfyRouting.ResolveTopic(global, null).Should().Be("shared-topic-1");
    }

    [Fact]
    public void ShouldPush_DuringQuietHours_OnlyLetsStopsThrough()
    {
        UserPreferences quiet = UserPreferences.Default with { QuietFromHour = 22, QuietToHour = 7 };

        NtfyRouting.ShouldPush(Alert(AlertKind.NewSuggestion), false, quiet, 23).Should().BeFalse();
        NtfyRouting.ShouldPush(Alert(AlertKind.TradeStopLoss), false, quiet, 23).Should().BeTrue();
        NtfyRouting.ShouldPush(Alert(AlertKind.NewSuggestion), false, quiet, 12).Should().BeTrue();
        NtfyRouting.ShouldPush(Alert(AlertKind.TradeStopLoss), true, quiet, 12).Should().BeFalse();
    }

    [Fact]
    public void ClickUrl_UsesThePublicAddressAndTheAlertLink()
    {
        NtfySettings withUrl = new(NtfySettings.DefaultServer, "topic-12345", "https://trader.example.com/");

        NtfyRouting.ClickUrl(withUrl, Alert(AlertKind.TradeTakeProfit)).Should().Be("https://trader.example.com/journal?trade=3");
        NtfyRouting.ClickUrl(withUrl with { PublicUrl = string.Empty }, Alert(AlertKind.TradeTakeProfit)).Should().BeNull();
    }

    [Fact]
    public void Validator_RejectsBadPublicUrlAndBadUserTopic()
    {
        NtfySettingsValidator.Validate(new NtfySettings(NtfySettings.DefaultServer, "topic-12345", "ftp://x")).Should().NotBeEmpty();
        NtfySettingsValidator.Validate(new NtfySettings(NtfySettings.DefaultServer, "topic-12345", "https://x.pt")).Should().BeEmpty();
        UserPreferencesValidator.Validate(UserPreferences.Default with { NtfyTopic = "x" }).Should().NotBeEmpty();
    }
}
