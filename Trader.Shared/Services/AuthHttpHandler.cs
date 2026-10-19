using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Trader.Shared.Services;

public sealed class AuthHttpHandler(AuthSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? token = await session.GetAccessTokenAsync();

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)
        {
            await session.LogoutAsync();
        }

        return response;
    }
}

public sealed class SessionAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly AuthSession _session;

    public SessionAuthenticationStateProvider(AuthSession session)
    {
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(BuildState());

    public void Dispose() => _session.Changed -= OnSessionChanged;

    private void OnSessionChanged() => NotifyAuthenticationStateChanged(Task.FromResult(BuildState()));

    private AuthenticationState BuildState() =>
        _session.IsAuthenticated
            ? new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, _session.Email ?? string.Empty)], "Bearer")))
            : Anonymous;
}
