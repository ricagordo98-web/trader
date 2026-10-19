using System.Globalization;
using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed class SuggestionEngine(ScoringOptions options)
{
    private const double AgreementFloor = 0.1;
    private const double BuzzFloor = 1.0;
    private const double BuzzCeiling = 3.0;

    public SuggestionDraft? Evaluate(MarketSnapshot market, SentimentSnapshot sentiment)
    {
        if (market.Atr <= 0 || market.Price <= 0)
        {
            return null;
        }

        List<SignalResult> signals = BuildSignals(market, sentiment);

        if (signals.Count < options.MinAgreeingSignals)
        {
            return null;
        }

        double score = signals.Sum(s => s.Weight * s.Value) / signals.Sum(s => s.Weight);

        if (Math.Abs(score) < 1e-9)
        {
            return null;
        }

        TradeDirection direction = score > 0 ? TradeDirection.Buy : TradeDirection.Sell;
        int sign = score > 0 ? 1 : -1;
        List<SignalResult> agreeing = signals.Where(s => Math.Sign(s.Value) == sign && Math.Abs(s.Value) >= AgreementFloor).ToList();

        if (agreeing.Count < options.MinAgreeingSignals)
        {
            return null;
        }

        double confidence = Sigmoid(options.ConfidenceSteepness * Math.Abs(score)) * agreeing.Count / signals.Count;

        if (confidence < options.ConfidenceThreshold)
        {
            return null;
        }

        (double stopLoss, double takeProfit) = ComputeLevels(market, direction);
        double risk = Math.Abs(market.Price - stopLoss);
        double riskReward = risk > 0 ? Math.Abs(takeProfit - market.Price) / risk : 0;

        if (riskReward < options.MinRiskReward)
        {
            return null;
        }

        IReadOnlyList<EvidenceArticle> articles = sentiment.Articles
            .Where(a => Math.Sign(a.Sentiment) == sign)
            .OrderByDescending(a => a.Influence)
            .Take(5)
            .ToList();

        return new SuggestionDraft(
            market.Symbol,
            market.Name,
            direction,
            confidence,
            score,
            market.Price,
            stopLoss,
            takeProfit,
            riskReward,
            signals,
            articles,
            BuildExplanation(direction, confidence, agreeing, signals.Count));
    }

    private List<SignalResult> BuildSignals(MarketSnapshot market, SentimentSnapshot sentiment)
    {
        List<SignalResult> signals = [];

        if (sentiment.ArticleCount >= options.MinSentimentArticles)
        {
            double value = Clamp(sentiment.WeightedSentiment / options.SentimentScale);
            signals.Add(new SignalResult(
                "sentiment",
                "Sentimento",
                value,
                options.SentimentWeight,
                $"sentimento {Format(sentiment.WeightedSentiment, "+0.00;-0.00")} em {sentiment.ArticleCount} artigos"));

            if (sentiment.BuzzZScore is { } z && z >= BuzzFloor && Math.Abs(value) >= AgreementFloor)
            {
                double buzz = Math.Sign(value) * Clamp(z / BuzzCeiling, 0, 1);
                signals.Add(new SignalResult(
                    "buzz",
                    "Volume de menções",
                    buzz,
                    options.BuzzWeight,
                    $"volume de menções {Format(z, "0.0")} desvios acima do normal"));
            }
        }

        signals.Add(BuildTrend(market));
        signals.Add(BuildMomentum(market));
        signals.Add(new SignalResult(
            "bollinger",
            "Bandas de Bollinger",
            Clamp((0.5 - market.PercentB) * 2),
            options.BollingerWeight,
            $"posição nas Bollinger %B {Format(market.PercentB, "0.00")}"));

        return signals;
    }

    private SignalResult BuildTrend(MarketSnapshot market)
    {
        List<double> parts =
        [
            Math.Sign(market.Price - market.Ema20),
            Math.Sign(market.Ema20 - market.Ema50)
        ];

        if (market.Ema200 is { } ema200)
        {
            parts.Add(Math.Sign(market.Ema50 - ema200));
        }

        double value = parts.Average();
        string detail = value switch
        {
            > 0 => "tendência de alta (preço acima das médias EMA20/50)",
            < 0 => "tendência de baixa (preço abaixo das médias EMA20/50)",
            _ => "tendência indefinida"
        };

        return new SignalResult("trend", "Tendência", value, options.TrendWeight, detail);
    }

    private SignalResult BuildMomentum(MarketSnapshot market)
    {
        double macd = Clamp(market.MacdHistogram / market.Atr * 5);
        double rsi = Clamp((50 - market.Rsi) / 25);
        double value = 0.6 * macd + 0.4 * rsi;

        string rsiTag = market.Rsi < 35 ? "sobrevenda" : market.Rsi > 65 ? "sobrecompra" : "neutro";
        string macdTag = market.MacdHistogram >= 0 ? "positivo" : "negativo";

        return new SignalResult(
            "momentum",
            "Momentum",
            value,
            options.MomentumWeight,
            $"RSI {Format(market.Rsi, "0")} ({rsiTag}) e MACD {macdTag}");
    }

    private (double StopLoss, double TakeProfit) ComputeLevels(MarketSnapshot market, TradeDirection direction)
    {
        double atr = market.Atr;
        double entry = market.Price;
        int sign = direction == TradeDirection.Buy ? 1 : -1;

        double stopLoss = entry - sign * options.SlAtrMultiple * atr;
        double takeProfit = entry + sign * options.TpAtrMultiple * atr;

        double swingStop = direction == TradeDirection.Buy ? market.SwingLow : market.SwingHigh;
        double stopDistance = Math.Abs(entry - swingStop) / atr;
        bool swingOnProtectiveSide = direction == TradeDirection.Buy ? swingStop < entry : swingStop > entry;

        if (swingOnProtectiveSide && stopDistance is >= 1.0 and <= 2.0)
        {
            stopLoss = swingStop - sign * 0.1 * atr;
        }

        double swingTarget = direction == TradeDirection.Buy ? market.SwingHigh : market.SwingLow;
        double risk = Math.Abs(entry - stopLoss);
        double targetDistance = Math.Abs(swingTarget - entry);
        bool swingOnTargetSide = direction == TradeDirection.Buy ? swingTarget > entry : swingTarget < entry;

        if (swingOnTargetSide && targetDistance >= options.MinRiskReward * risk && targetDistance < Math.Abs(takeProfit - entry))
        {
            takeProfit = swingTarget;
        }

        return (stopLoss, takeProfit);
    }

    private static string BuildExplanation(TradeDirection direction, double confidence, List<SignalResult> agreeing, int total)
    {
        string verb = direction == TradeDirection.Buy ? "Compra" : "Venda";
        string reasons = string.Join("; ", agreeing.OrderByDescending(s => s.Weight * Math.Abs(s.Value)).Select(s => s.Detail));
        int percent = (int)Math.Round(confidence * 100);

        return $"{verb} sugerida com {percent}% de certeza: {reasons}. {agreeing.Count} de {total} sinais concordam.";
    }

    private static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));

    private static double Clamp(double value, double min = -1, double max = 1) => Math.Min(max, Math.Max(min, value));

    private static string Format(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
