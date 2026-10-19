using System.Text.RegularExpressions;

namespace Trader.Infrastructure.News;

public static class DynamicSymbolMatcher
{
    private const int MinBareSymbolLength = 3;

    public static Regex Build(IEnumerable<string> symbols)
    {
        string[] plain = symbols
            .Where(s => s.All(char.IsLetterOrDigit))
            .Select(s => Regex.Escape(s.ToUpperInvariant()))
            .ToArray();

        if (plain.Length == 0)
        {
            return new Regex("(?!)");
        }

        string bare = string.Join('|', plain.Where(s => s.Length >= MinBareSymbolLength));
        string all = string.Join('|', plain);
        string pattern = bare.Length == 0
            ? $@"\$(?i:(?<symbol>{all}))\b"
            : $@"\$(?i:(?<symbol>{all}))\b|\b(?<symbol>{bare})\b";

        return new Regex(pattern, RegexOptions.CultureInvariant);
    }

    public static IEnumerable<string> Match(Regex matcher, string text) =>
        matcher.Matches(text).Select(m => m.Groups["symbol"].Value.ToUpperInvariant()).Distinct();
}
