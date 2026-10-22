using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Core.Abstractions;
using Trader.Core.Explain;
using Trader.Infrastructure.Summary;

namespace Trader.Infrastructure.Explain;

public sealed class OllamaMoveNarrator(HttpClient httpClient, IOptions<SummaryOptions> options, ILogger<OllamaMoveNarrator> logger) : IMoveNarrator
{
    private const int MaxAttempts = 2;

    private readonly SummaryOptions _options = options.Value;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.OllamaModel);

    public string Name => $"modelo local ({_options.OllamaModel})";

    public async Task<NarrationResult> NarrateAsync(MoveAnalysis analysis, IReadOnlyList<MoveCause> causes, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new NarrationResult(null, NarrativeStatus.Disabled, null, []);
        }

        string prompt = NarrationPrompt.Build(NarrationPrompt.Facts(analysis, causes));
        IReadOnlyList<string> violations = [];

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string? raw = await GenerateAsync(attempt == 1 ? prompt : $"{prompt}\n\nA resposta anterior foi rejeitada: {string.Join("; ", violations)}. Responde de novo usando só os factos.", cancellationToken);

            if (raw is null)
            {
                return new NarrationResult(null, NarrativeStatus.Unavailable, Name, []);
            }

            NarrationDraft? draft = Parse(raw);

            if (draft is null)
            {
                violations = ["resposta não é JSON válido"];
                continue;
            }

            violations = NarrationGuard.Validate(draft, analysis, causes);

            if (violations.Count == 0)
            {
                return new NarrationResult(draft.Summary.Trim(), NarrativeStatus.Accepted, Name, []);
            }
        }

        return new NarrationResult(null, NarrativeStatus.Rejected, Name, violations);
    }

    internal static NarrationDraft? Parse(string raw)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(NarrationGuard.StripToJson(raw));
            JsonElement root = document.RootElement;

            string summary = root.TryGetProperty("summary", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? string.Empty : string.Empty;
            string confidence = (root.TryGetProperty("confidence", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty)
                .Trim().ToLowerInvariant().Replace("é", "e");
            List<string> used = root.TryGetProperty("used", out JsonElement u) && u.ValueKind == JsonValueKind.Array
                ? u.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim('[', ']', ' ')).ToList()
                : [];

            return new NarrationDraft(summary, confidence, used);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                $"{_options.OllamaUrl.TrimEnd('/')}/api/generate",
                new
                {
                    model = _options.OllamaModel,
                    prompt,
                    stream = false,
                    format = "json",
                    keep_alive = "2m",
                    options = new { temperature = 0, seed = 7, num_ctx = 4096, num_predict = 450 }
                },
                cancellationToken);
            response.EnsureSuccessStatusCode();

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("response", out JsonElement element) ? element.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Local model unavailable for the move explanation: {Message}", ex.Message);
            return null;
        }
    }
}
