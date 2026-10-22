using System.Text.Json.Nodes;
using Trader.Core.Intraday;

namespace Trader.Core.Models;

public sealed record DisplayConversion(string Currency, IReadOnlyDictionary<string, double> Factors)
{
    public static readonly DisplayConversion None = new(string.Empty, new Dictionary<string, double>());

    public bool Applies(string symbol) => Factors.ContainsKey(symbol);

    public double FactorFor(string symbol) => Factors.TryGetValue(symbol, out double factor) ? factor : 1;

    public IReadOnlyList<Candle> Apply(string symbol, IReadOnlyList<Candle> candles)
    {
        double factor = FactorFor(symbol);

        return factor == 1
            ? candles
            : candles.Select(c => c with { Open = c.Open * factor, High = c.High * factor, Low = c.Low * factor, Close = c.Close * factor }).ToList();
    }

    public InstrumentQuote Apply(InstrumentQuote quote)
    {
        if (!Applies(quote.Symbol))
        {
            return quote;
        }

        double factor = FactorFor(quote.Symbol);

        return quote with
        {
            Currency = Currency,
            Price = quote.Price * factor,
            Change = quote.Change * factor,
            Sparkline = quote.Sparkline.Select(p => p * factor).ToList()
        };
    }

    public SuggestionDto Apply(SuggestionDto suggestion)
    {
        if (!Applies(suggestion.Symbol))
        {
            return suggestion;
        }

        double factor = FactorFor(suggestion.Symbol);

        return suggestion with
        {
            Entry = suggestion.Entry * factor,
            StopLoss = suggestion.StopLoss * factor,
            TakeProfit = suggestion.TakeProfit * factor,
            ExitPrice = suggestion.ExitPrice * factor,
            DisplayFactor = factor
        };
    }

    public RadarSnapshot Apply(RadarSnapshot snapshot) => snapshot with
    {
        Events = snapshot.Events.Select(e => e with { Price = e.Price * FactorFor(e.Symbol) }).ToList(),
        Board = snapshot.Board.Select(r => r with { Price = r.Price * FactorFor(r.Symbol) }).ToList()
    };

    public Explain.MoveExplanation Apply(Explain.MoveExplanation explanation)
    {
        double factor = FactorFor(explanation.Analysis.Symbol);

        return factor == 1
            ? explanation
            : explanation with { Analysis = explanation.Analysis with { StartPrice = explanation.Analysis.StartPrice * factor, EndPrice = explanation.Analysis.EndPrice * factor } };
    }

    public string ToDisplayDrawings(string symbol, string json) => ScaleDrawings(json, FactorFor(symbol));

    public string ToStoredDrawings(string symbol, string json) => ScaleDrawings(json, 1 / FactorFor(symbol));

    private static string ScaleDrawings(string json, double factor)
    {
        if (factor == 1)
        {
            return json;
        }

        JsonNode? root = JsonNode.Parse(json);
        Scale(root, factor);
        return root?.ToJsonString() ?? json;
    }

    private static void Scale(JsonNode? node, double factor)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Scale(item, factor);
                }

                break;
            case JsonObject obj:
                foreach ((string name, JsonNode? child) in obj.ToList())
                {
                    if (name == "points" && child is JsonArray points)
                    {
                        foreach (JsonNode? point in points)
                        {
                            if (point is JsonObject pointObject && pointObject["value"] is JsonValue value && value.TryGetValue(out double number))
                            {
                                pointObject["value"] = number * factor;
                            }
                        }
                    }
                    else
                    {
                        Scale(child, factor);
                    }
                }

                break;
        }
    }
}
