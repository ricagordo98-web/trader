using System.Globalization;
using Trader.Core.Models;

namespace Trader.Core.Market;

public enum SentimentFilter
{
    Any,
    Positive,
    Neutral,
    Negative
}

public enum ScreenerSort
{
    Symbol,
    Change,
    Sentiment,
    Articles,
    Confidence
}

public sealed record ScreenerRow(
    InstrumentQuote Quote,
    double? Sentiment,
    int ArticleCount,
    SuggestionDto? Suggestion,
    DateOnly? EarningsDate);

public sealed record ScreenerFilter(
    AssetClass? AssetClass = null,
    double MinAbsChangePercent = 0,
    SentimentFilter Sentiment = SentimentFilter.Any,
    bool OnlyWithSuggestion = false,
    bool OnlyEarningsSoon = false,
    string? Search = null);

public static class Screener
{
    public static IReadOnlyList<ScreenerRow> Build(
        IReadOnlyList<InstrumentQuote> quotes,
        IReadOnlyList<SentimentSummary> sentiment,
        IReadOnlyList<SuggestionDto> openSuggestions,
        IReadOnlyDictionary<string, DateOnly> earnings)
    {
        Dictionary<string, SentimentSummary> sentimentBySymbol = sentiment.ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SuggestionDto> suggestionBySymbol = openSuggestions
            .GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Confidence).First(), StringComparer.OrdinalIgnoreCase);

        return quotes
            .Select(q => new ScreenerRow(
                q,
                sentimentBySymbol.TryGetValue(q.Symbol, out SentimentSummary? s) && s.ArticleCount > 0 ? s.AverageSentiment : null,
                sentimentBySymbol.TryGetValue(q.Symbol, out SentimentSummary? c) ? c.ArticleCount : 0,
                suggestionBySymbol.GetValueOrDefault(q.Symbol),
                earnings.TryGetValue(q.Symbol, out DateOnly date) ? date : null))
            .ToList();
    }

    public static IReadOnlyList<ScreenerRow> Apply(IEnumerable<ScreenerRow> rows, ScreenerFilter filter, ScreenerSort sort, bool descending)
    {
        IEnumerable<ScreenerRow> query = rows;

        if (filter.AssetClass is { } assetClass)
        {
            query = query.Where(r => r.Quote.AssetClass == assetClass);
        }

        if (filter.MinAbsChangePercent > 0)
        {
            query = query.Where(r => Math.Abs(r.Quote.ChangePercent ?? 0) >= filter.MinAbsChangePercent);
        }

        query = filter.Sentiment switch
        {
            SentimentFilter.Positive => query.Where(r => SentimentBands.IsPositive(r.Sentiment)),
            SentimentFilter.Negative => query.Where(r => SentimentBands.IsNegative(r.Sentiment)),
            SentimentFilter.Neutral => query.Where(r => r.Sentiment is not null && !SentimentBands.IsPositive(r.Sentiment) && !SentimentBands.IsNegative(r.Sentiment)),
            _ => query
        };

        if (filter.OnlyWithSuggestion)
        {
            query = query.Where(r => r.Suggestion is not null);
        }

        if (filter.OnlyEarningsSoon)
        {
            query = query.Where(r => r.EarningsDate is not null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();
            query = query.Where(r => r.Quote.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase) || r.Quote.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Func<ScreenerRow, IComparable?> key = sort switch
        {
            ScreenerSort.Change => r => r.Quote.ChangePercent,
            ScreenerSort.Sentiment => r => r.Sentiment,
            ScreenerSort.Articles => r => r.ArticleCount,
            ScreenerSort.Confidence => r => r.Suggestion?.Confidence,
            _ => r => r.Quote.Symbol
        };

        IOrderedEnumerable<ScreenerRow> ordered = descending
            ? query.OrderBy(r => key(r) is null).ThenByDescending(key)
            : query.OrderBy(r => key(r) is null).ThenBy(key);

        return ordered.ToList();
    }

    public static string HeatColor(double? changePercent, bool dark)
    {
        if (changePercent is not { } change)
        {
            return dark ? "rgba(148,163,184,0.12)" : "rgba(100,116,139,0.12)";
        }

        double intensity = Math.Clamp(Math.Abs(change) / 3.0, 0, 1);
        double alpha = 0.12 + intensity * 0.68;
        string rgb = change >= 0 ? "34,197,94" : "239,68,68";

        return string.Create(CultureInfo.InvariantCulture, $"rgba({rgb},{alpha:0.00})");
    }
}
