using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Trader.Core.Charts;

namespace Trader.Tests;

public class ChartTemplateValidatorTests
{
    [Fact]
    public void Validate_ValidTemplates_ReturnsNoErrors() =>
        ChartTemplateValidator.Validate([new ChartTemplate("Tendência", ["EMA", "VOL"]), new ChartTemplate("Momentum", ["RSI", "MACD"])]).Should().BeEmpty();

    [Fact]
    public void Validate_DuplicateNamesIgnoringCase_ReturnsError() =>
        ChartTemplateValidator.Validate([new ChartTemplate("A", []), new ChartTemplate("a", [])]).Should().NotBeEmpty();

    [Fact]
    public void Validate_UnknownIndicator_ReturnsError() =>
        ChartTemplateValidator.Validate([new ChartTemplate("A", ["NOPE"])]).Should().NotBeEmpty();

    [Fact]
    public void Validate_EmptyOrLongName_ReturnsError()
    {
        ChartTemplateValidator.Validate([new ChartTemplate(" ", [])]).Should().NotBeEmpty();
        ChartTemplateValidator.Validate([new ChartTemplate(new string('x', 41), [])]).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_TooManyTemplates_ReturnsError() =>
        ChartTemplateValidator.Validate(Enumerable.Range(0, 21).Select(i => new ChartTemplate($"T{i}", [])).ToList()).Should().NotBeEmpty();
}

public class ChartTemplateIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(TraderFactory factory, string email, HttpClient? creator = null)
    {
        HttpClient client = factory.CreateClient();
        (await (creator ?? client).PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Templates_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().GetAsync("/api/chart/templates")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Templates_SaveAndLoad_AreStoredPerUser()
    {
        await using TraderFactory factory = new();
        HttpClient first = await SignIn(factory, "a@example.com");
        HttpClient second = await SignIn(factory, "b@example.com", first);

        (await first.PutAsJsonAsync("/api/chart/templates", new[] { new ChartTemplate(" Tendência ", ["EMA", "VOL"]) })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        List<ChartTemplate> mine = (await first.GetFromJsonAsync<List<ChartTemplate>>("/api/chart/templates"))!;
        mine.Should().ContainSingle().Which.Name.Should().Be("Tendência");
        (await second.GetFromJsonAsync<List<ChartTemplate>>("/api/chart/templates"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task Templates_InvalidIndicator_ReturnsBadRequest()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory, "a@example.com");

        (await client.PutAsJsonAsync("/api/chart/templates", new[] { new ChartTemplate("X", ["NOPE"]) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
