using System.Text.RegularExpressions;
using Trader.Core.Abstractions;

namespace Trader.Infrastructure.News;

public sealed class TickerExtractor : ITickerExtractor
{
    private sealed record Rule(string Symbol, Regex Pattern);

    private static readonly Rule[] Rules = BuildRules();

    public IReadOnlyList<string> Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<string> symbols = [];

        foreach (Rule rule in Rules)
        {
            if (!symbols.Contains(rule.Symbol) && rule.Pattern.IsMatch(text))
            {
                symbols.Add(rule.Symbol);
            }
        }

        return symbols;
    }

    private static Rule[] BuildRules()
    {
        (string Symbol, string Ticker, string[] Names)[] stocks =
        [
            ("AAPL", "AAPL", ["Apple"]),
            ("MSFT", "MSFT", ["Microsoft"]),
            ("NVDA", "NVDA", ["Nvidia", "NVIDIA"]),
            ("TSLA", "TSLA", ["Tesla"]),
            ("AMZN", "AMZN", ["Amazon"]),
            ("GOOGL", "GOOGL?", ["Alphabet", "Google"]),
            ("META", "META", ["Meta Platforms", "Facebook", "Instagram"]),
            ("AMD", "AMD", ["Advanced Micro Devices"]),
            ("NFLX", "NFLX", ["Netflix"]),
            ("JPM", "JPM", ["JPMorgan", "JP Morgan"]),
            ("SPY", "SPY", ["S&P 500", "S&P500"]),
            ("QQQ", "QQQ", ["Nasdaq 100", "Nasdaq-100"]),
            ("VOO", "VOO", []),
            ("IWM", "IWM", ["Russell 2000"]),
            ("GLD", "GLD", []),
            ("TLT", "TLT", []),
            ("XLK", "XLK", []),
            ("ARKK", "ARKK", ["ARK Invest"])
        ];

        (string Symbol, string Pattern)[] insensitive =
        [
            ("GC=F", @"gold(?! ?(?:man|en|stein))|bullion"),
            ("SI=F", @"silver"),
            ("CL=F", @"crude|WTI|oil prices?"),
            ("BZ=F", @"brent"),
            ("NG=F", @"natural gas|nat-gas"),
            ("HG=F", @"copper"),
            ("EURUSD=X", @"EUR/USD|EURUSD|euro-dollar"),
            ("GBPUSD=X", @"GBP/USD|GBPUSD|sterling"),
            ("USDJPY=X", @"USD/JPY|USDJPY|\byen\b"),
            ("USDCHF=X", @"USD/CHF|USDCHF|swiss franc"),
            ("AUDUSD=X", @"AUD/USD|AUDUSD|aussie dollar"),
            ("USDCAD=X", @"USD/CAD|USDCAD|loonie"),
            ("EURGBP=X", @"EUR/GBP|EURGBP")
        ];

        List<Rule> rules = [];

        foreach ((string symbol, string ticker, string[] names) in stocks)
        {
            string alternatives = string.Join('|', new[] { ticker }.Concat(names.Select(Regex.Escape)).Select(a => $@"\b{a}\b"));
            rules.Add(new Rule(symbol, new Regex(
                $@"\$(?i:{ticker})\b|{alternatives}",
                RegexOptions.Compiled | RegexOptions.CultureInvariant)));
        }

        foreach ((string symbol, string pattern) in insensitive)
        {
            rules.Add(new Rule(symbol, new Regex(
                $@"\b(?:{pattern})\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)));
        }

        return rules.ToArray();
    }
}
