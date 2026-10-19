using System.Net;
using System.Text.RegularExpressions;

namespace Trader.Infrastructure.News.Sources;

public static partial class TextCleaner
{
    public static string Clean(string? html, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        string text = WebUtility.HtmlDecode(HtmlTag().Replace(html, " "));
        text = Whitespace().Replace(text, " ").Trim();

        return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
