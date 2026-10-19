using System.Text.Json;

namespace Trader.Shared.Services;

public static class ApiError
{
    public static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        try
        {
            string body = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Object)
            {
                IEnumerable<string> messages = errors.EnumerateObject()
                    .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(v => v.GetString()) : [p.Value.GetString()])
                    .OfType<string>();

                string joined = string.Join(" ", messages);

                if (joined.Length > 0)
                {
                    return joined;
                }
            }

            if (root.TryGetProperty("title", out JsonElement title) && title.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
        }

        return $"O servidor respondeu com o erro {(int)response.StatusCode}.";
    }
}
