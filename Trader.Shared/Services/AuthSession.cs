using System.Net.Http.Json;
using System.Text.Json;

namespace Trader.Shared.Services;

public sealed class AuthSession(IClientStore store, HttpClient anonymousClient)
{
    private const string StoreKey = "auth.session";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private StoredSession? _session;

    public event Action? Changed;

    public bool Initialized { get; private set; }

    public bool IsAuthenticated => _session is not null;

    public string? Email => _session?.Email;

    public async Task InitializeAsync()
    {
        if (Initialized)
        {
            return;
        }

        try
        {
            string? json = await store.GetAsync(StoreKey);
            _session = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<StoredSession>(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _session = null;
        }

        Initialized = true;
        Changed?.Invoke();
    }

    public async Task<bool> IsRegistrationOpenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            AuthStatus? status = await anonymousClient.GetFromJsonAsync<AuthStatus>("api/auth/status", cancellationToken);
            return status?.RegistrationOpen ?? false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<string?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendCredentialsAsync("api/auth/login", email, password, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            {
                return await ApiError.ReadAsync(response);
            }

            return (await response.Content.ReadAsStringAsync(cancellationToken)).Contains("LockedOut", StringComparison.Ordinal)
                ? "Conta temporariamente bloqueada por demasiadas tentativas. Tenta novamente dentro de alguns minutos."
                : "Email ou palavra-passe incorretos.";
        }

        TokenResponse? tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

        if (tokens is null)
        {
            return "Resposta inválida do servidor.";
        }

        await SetSessionAsync(new StoredSession(email, tokens.AccessToken, tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn)));
        return null;
    }

    public async Task<string?> RegisterFirstAccountAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendCredentialsAsync("api/auth/register", email, password, cancellationToken);

        return response.IsSuccessStatusCode
            ? await LoginAsync(email, password, cancellationToken)
            : await ApiError.ReadAsync(response);
    }

    public async Task LogoutAsync()
    {
        _session = null;
        await store.RemoveAsync(StoreKey);
        Changed?.Invoke();
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        StoredSession? session = _session;

        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
        {
            return session.AccessToken;
        }

        return await RefreshAsync(session) ? _session?.AccessToken : null;
    }

    private async Task<bool> RefreshAsync(StoredSession stale)
    {
        await _refreshLock.WaitAsync();

        try
        {
            if (_session is not null && _session != stale)
            {
                return true;
            }

            using HttpResponseMessage response = await anonymousClient.PostAsJsonAsync("api/auth/refresh", new { refreshToken = stale.RefreshToken });

            if (!response.IsSuccessStatusCode)
            {
                await LogoutAsync();
                return false;
            }

            TokenResponse? tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();

            if (tokens is null)
            {
                await LogoutAsync();
                return false;
            }

            await SetSessionAsync(new StoredSession(stale.Email, tokens.AccessToken, tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn)));
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendCredentialsAsync(string url, string email, string password, CancellationToken cancellationToken)
    {
        try
        {
            return await anonymousClient.PostAsJsonAsync(url, new { email, password }, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { title = "Não foi possível contactar o servidor." })
            };
        }
    }

    private async Task SetSessionAsync(StoredSession session)
    {
        _session = session;
        await store.SetAsync(StoreKey, JsonSerializer.Serialize(session));
        Changed?.Invoke();
    }

    private sealed record StoredSession(string Email, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);

    private sealed record AuthStatus(bool RegistrationOpen);
}
