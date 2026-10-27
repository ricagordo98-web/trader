using Trader.Core.Intraday;

namespace Trader.Core.Scoring;

public static class SignalWeightMap
{
    public static IReadOnlyDictionary<string, double> Read(IntradayOptions o) => new Dictionary<string, double>
    {
        ["vwap"] = o.VwapWeight,
        ["opening-range"] = o.OpeningRangeWeight,
        ["intraday-momentum"] = o.MomentumWeight,
        ["relative-volume"] = o.VolumeWeight,
        ["previous-day"] = o.PreviousDayWeight,
        ["gap"] = o.GapWeight,
        ["recent-news"] = o.NewsWeight,
        ["catalyst"] = o.CatalystWeight,
        ["market"] = o.MarketWeight
    };

    public static void Apply(IntradayOptions o, IReadOnlyDictionary<string, double> weights)
    {
        foreach ((string name, double weight) in weights)
        {
            switch (name)
            {
                case "vwap": o.VwapWeight = weight; break;
                case "opening-range": o.OpeningRangeWeight = weight; break;
                case "intraday-momentum": o.MomentumWeight = weight; break;
                case "relative-volume": o.VolumeWeight = weight; break;
                case "previous-day": o.PreviousDayWeight = weight; break;
                case "gap": o.GapWeight = weight; break;
                case "recent-news": o.NewsWeight = weight; break;
                case "catalyst": o.CatalystWeight = weight; break;
                case "market": o.MarketWeight = weight; break;
            }
        }
    }

    public static IReadOnlyDictionary<string, double> Read(ScoringOptions o) => new Dictionary<string, double>
    {
        ["sentiment"] = o.SentimentWeight,
        ["buzz"] = o.BuzzWeight,
        ["trend"] = o.TrendWeight,
        ["momentum"] = o.MomentumWeight,
        ["bollinger"] = o.BollingerWeight,
        ["relative-strength"] = o.RelativeStrengthWeight
    };

    public static void Apply(ScoringOptions o, IReadOnlyDictionary<string, double> weights)
    {
        foreach ((string name, double weight) in weights)
        {
            switch (name)
            {
                case "sentiment": o.SentimentWeight = weight; break;
                case "buzz": o.BuzzWeight = weight; break;
                case "trend": o.TrendWeight = weight; break;
                case "momentum": o.MomentumWeight = weight; break;
                case "bollinger": o.BollingerWeight = weight; break;
                case "relative-strength": o.RelativeStrengthWeight = weight; break;
            }
        }
    }
}
