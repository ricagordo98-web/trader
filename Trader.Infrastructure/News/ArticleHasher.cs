using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Trader.Infrastructure.News;

public static partial class ArticleHasher
{
    public static string HashUrl(string url)
    {
        string normalized = url.Trim();

        if (Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri))
        {
            string query = string.Join('&', uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !IsTrackingParameter(p)));

            normalized = $"{uri.Host.ToLowerInvariant()}{uri.AbsolutePath.TrimEnd('/')}{(query.Length > 0 ? "?" + query : string.Empty)}";
        }

        return Sha256(normalized);
    }

    public static bool IsRedundantSummary(string title, string summary) =>
        summary.Length == 0 || NonAlphanumeric().Replace(summary.ToLowerInvariant(), string.Empty).StartsWith(NonAlphanumeric().Replace(title.ToLowerInvariant(), string.Empty), StringComparison.Ordinal);

    public static string HashTitle(string title) =>
        Sha256(NonAlphanumeric().Replace(title.ToLowerInvariant(), string.Empty));

    private static bool IsTrackingParameter(string parameter) =>
        parameter.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
        || parameter.StartsWith("fbclid", StringComparison.OrdinalIgnoreCase)
        || parameter.StartsWith("gclid", StringComparison.OrdinalIgnoreCase)
        || parameter.StartsWith("ref=", StringComparison.OrdinalIgnoreCase)
        || parameter.StartsWith("cid=", StringComparison.OrdinalIgnoreCase);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
