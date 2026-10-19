using System.Net.Http.Json;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Shared.Services;

public sealed class TraderApiClient(HttpClient httpClient)
{
    public Uri NewsHubUrl => new(httpClient.BaseAddress!, "hubs/news");

    public async Task<IReadOnlyList<InstrumentQuote>> GetInstrumentsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<InstrumentQuote>>("api/instruments", cancellationToken) ?? [];

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, string range, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<Candle>>(
            $"api/instruments/{Uri.EscapeDataString(symbol)}/candles?interval={interval}&range={range}",
            cancellationToken) ?? [];

    public async Task<string> GetDrawingsAsync(string symbol, CancellationToken cancellationToken = default) =>
        await httpClient.GetStringAsync($"api/instruments/{Uri.EscapeDataString(symbol)}/drawings", cancellationToken);

    public async Task SaveDrawingsAsync(string symbol, string json, CancellationToken cancellationToken = default)
    {
        using StringContent content = new(json, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.PutAsync($"api/instruments/{Uri.EscapeDataString(symbol)}/drawings", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ArticleDto>> GetNewsAsync(NewsQuery query, CancellationToken cancellationToken = default)
    {
        List<string> parameters = [$"limit={query.Limit}"];

        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            parameters.Add($"symbol={Uri.EscapeDataString(query.Symbol)}");
        }

        if (query.SourceType is { } sourceType)
        {
            parameters.Add($"sourceType={sourceType}");
        }

        if (!string.IsNullOrWhiteSpace(query.Sentiment))
        {
            parameters.Add($"sentiment={Uri.EscapeDataString(query.Sentiment)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        if (query.Before is { } before)
        {
            parameters.Add($"before={Uri.EscapeDataString(before.ToString("O"))}");
        }

        return await httpClient.GetFromJsonAsync<IReadOnlyList<ArticleDto>>($"api/news?{string.Join('&', parameters)}", cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<SentimentSummary>> GetSentimentSummaryAsync(int hours = 24, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<SentimentSummary>>($"api/news/sentiment?hours={hours}", cancellationToken) ?? [];

    public async Task<IReadOnlyList<SuggestionDto>> GetSuggestionsAsync(string? symbol, bool? open, int limit = 100, CancellationToken cancellationToken = default)
    {
        List<string> parameters = [$"limit={limit}"];

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            parameters.Add($"symbol={Uri.EscapeDataString(symbol)}");
        }

        if (open is { } isOpen)
        {
            parameters.Add($"open={isOpen.ToString().ToLowerInvariant()}");
        }

        return await httpClient.GetFromJsonAsync<IReadOnlyList<SuggestionDto>>($"api/suggestions?{string.Join('&', parameters)}", cancellationToken) ?? [];
    }

    public async Task<TrackRecordDto> GetTrackRecordAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<TrackRecordDto>("api/suggestions/track-record", cancellationToken)
        ?? new TrackRecordDto(0, 0, 0, 0, 0, 0, null, [], [], []);

    public async Task RefreshSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync("api/suggestions/refresh", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<WatchlistItem>>("api/watchlist", cancellationToken) ?? [];

    public async Task<string?> AddInstrumentAsync(AddInstrumentRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("api/watchlist", request, cancellationToken);
        return response.IsSuccessStatusCode ? null : await ApiError.ReadAsync(response);
    }

    public async Task<bool> RemoveInstrumentAsync(string symbol, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.DeleteAsync($"api/watchlist/{Uri.EscapeDataString(symbol)}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<SourceStatus>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<SourceStatus>>("api/sources", cancellationToken) ?? [];

    public async Task<ScoringOptions> GetScoringAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ScoringOptions>("api/settings/scoring", cancellationToken) ?? new ScoringOptions();

    public async Task<string?> SaveScoringAsync(ScoringOptions options, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync("api/settings/scoring", options, cancellationToken);
        return response.IsSuccessStatusCode ? null : await ApiError.ReadAsync(response);
    }

    public async Task ResetScoringAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.DeleteAsync("api/settings/scoring", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> ChangePasswordAsync(string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("api/auth/manage/info", new { oldPassword, newPassword }, cancellationToken);
        return response.IsSuccessStatusCode ? null : await ApiError.ReadAsync(response);
    }

    public async Task<string?> CreateUserAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("api/auth/register", new { email, password }, cancellationToken);
        return response.IsSuccessStatusCode ? null : await ApiError.ReadAsync(response);
    }
}
