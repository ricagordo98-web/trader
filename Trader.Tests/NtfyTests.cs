using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trader.Core.Abstractions;
using Trader.Core.Alerts;
using Trader.Infrastructure.Alerts;

namespace Trader.Tests;

public class NtfySettingsValidatorTests
{
    [Theory]
    [InlineData("https://ntfy.sh", "trader-abc123xyz")]
    [InlineData("https://ntfy.sh", "")]
    [InlineData("", "")]
    public void Validate_ValidSettings_ReturnsNoErrors(string server, string topic) =>
        NtfySettingsValidator.Validate(new NtfySettings(server, topic)).Should().BeEmpty();

    [Theory]
    [InlineData("https://ntfy.sh", "curto")]
    [InlineData("https://ntfy.sh", "tem espacos no topico")]
    [InlineData("http://ntfy.sh", "trader-abc123xyz")]
    [InlineData("nao-e-url", "trader-abc123xyz")]
    public void Validate_InvalidSettings_ReturnsError(string server, string topic) =>
        NtfySettingsValidator.Validate(new NtfySettings(server, topic)).Should().NotBeEmpty();
}

public class NtfyPublisherTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(Uri? Uri, string Body)> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status);
        }
    }

    private static AlertDto Alert(AlertKind kind) => new(1, kind, "AAPL", "Stop loss atingido: AAPL", "Resultado -1.00 R", 5, DateTimeOffset.UtcNow, true);

    [Fact]
    public async Task PublishAsync_StopLoss_PostsJsonToServerWithHighPriority()
    {
        CapturingHandler handler = new();
        NtfyPublisher publisher = new(new HttpClient(handler));

        await publisher.PublishAsync(new NtfySettings("https://ntfy.example", "trader-abc123xyz"), [Alert(AlertKind.StopLoss)], CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri!.ToString().Should().Be("https://ntfy.example/");
        JsonElement body = JsonDocument.Parse(handler.Requests[0].Body).RootElement;
        body.GetProperty("topic").GetString().Should().Be("trader-abc123xyz");
        body.GetProperty("title").GetString().Should().Be("Stop loss atingido: AAPL");
        body.GetProperty("priority").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task PublishAsync_ServerError_Throws()
    {
        CapturingHandler handler = new() { Status = HttpStatusCode.InternalServerError };
        NtfyPublisher publisher = new(new HttpClient(handler));

        Func<Task> act = () => publisher.PublishAsync(new NtfySettings("https://ntfy.example", "trader-abc123xyz"), [Alert(AlertKind.NewSuggestion)], CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}

public class NtfyIntegrationTests
{
    private const string Password = "Str0ng!Passw0rd";

    private sealed record Tokens(string AccessToken);

    private static async Task<HttpClient> SignIn(HttpClient client)
    {
        (await client.PostAsJsonAsync("/api/auth/register", new { email = "a@example.com", password = Password })).EnsureSuccessStatusCode();
        Tokens tokens = (await (await client.PostAsJsonAsync("/api/auth/login", new { email = "a@example.com", password = Password })).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Ntfy_WithoutToken_ReturnsUnauthorized()
    {
        await using TraderFactory factory = new();

        (await factory.CreateClient().GetAsync("/api/alerts/ntfy")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SaveNtfy_InvalidTopic_ReturnsBadRequest()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory.CreateClient());

        (await client.PutAsJsonAsync("/api/alerts/ntfy", new NtfySettings("https://ntfy.sh", "x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SaveNtfy_ValidSettings_AreReturnedByGet()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory.CreateClient());

        (await client.PutAsJsonAsync("/api/alerts/ntfy", new NtfySettings("https://ntfy.sh/", " trader-abc123xyz "))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        NtfySettings saved = (await client.GetFromJsonAsync<NtfySettings>("/api/alerts/ntfy"))!;
        saved.Should().Be(new NtfySettings("https://ntfy.sh", "trader-abc123xyz"));
    }

    [Fact]
    public async Task SendTest_WithoutTopic_ReturnsBadRequest()
    {
        await using TraderFactory factory = new();
        HttpClient client = await SignIn(factory.CreateClient());

        (await client.PostAsync("/api/alerts/ntfy/test", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Raise_WithTopicConfigured_PublishesAndSurvivesPublisherFailure()
    {
        IAlertPublisher publisher = Substitute.For<IAlertPublisher>();
        publisher.PublishAsync(Arg.Any<NtfySettings>(), Arg.Any<IReadOnlyList<AlertDto>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("down")));

        await using TraderFactory factory = new();
        WebApplicationFactory<Program> overridden = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton(publisher)));
        HttpClient client = await SignIn(overridden.CreateClient());
        await client.PutAsJsonAsync("/api/alerts/ntfy", new NtfySettings("https://ntfy.sh", "trader-abc123xyz"));

        await using AsyncServiceScope services = overridden.Services.CreateAsyncScope();
        IAlertService alerts = services.ServiceProvider.GetRequiredService<IAlertService>();

        Func<Task> act = () => alerts.RaiseAsync([new Alert { Kind = AlertKind.NewSuggestion, Symbol = "AAPL", Title = "t", Message = "m", CreatedAt = DateTimeOffset.UtcNow }], CancellationToken.None);

        await act.Should().NotThrowAsync();
        await publisher.Received(1).PublishAsync(Arg.Is<NtfySettings>(s => s.Topic == "trader-abc123xyz"), Arg.Any<IReadOnlyList<AlertDto>>(), Arg.Any<CancellationToken>());
    }
}
