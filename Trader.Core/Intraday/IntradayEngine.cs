using System.Globalization;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Core.Intraday;

public sealed class IntradayEngine(IntradayOptions options)
{
    private const double AgreementFloor = 0.1;
    private static readonly TimeSpan GapEventWindow = TimeSpan.FromHours(3);

    public IReadOnlyList<RadarEvent> DetectEvents(IntradayContext context)
    {
        List<RadarEvent> events = [];
        double? rvol = context.RelativeVolume;
        TradeDirection daily = context.Price >= context.DayOpen ? TradeDirection.Buy : TradeDirection.Sell;

        if (rvol is { } ratio && ratio >= options.VolumeSpikeRelativeVolume)
        {
            events.Add(Event(context, RadarEventKind.VolumeSpike, daily, Clamp((ratio - 1) / 5, 0, 1), $"volume {Format(ratio, "0.0")}× acima do normal para esta hora"));
        }

        if (context.GapPercent is { } gap && Math.Abs(gap) >= options.GapEventPercent && context.LastCandleTime - context.SessionStart <= GapEventWindow)
        {
            events.Add(Event(
                context,
                RadarEventKind.Gap,
                gap > 0 ? TradeDirection.Buy : TradeDirection.Sell,
                Clamp(Math.Abs(gap) / 6, 0, 1),
                $"gap de {Format(gap, "+0.0;-0.0")}% na abertura"));
        }

        bool volumeOk = rvol is null || rvol >= options.MinRelativeVolume;
        double buffer = options.BreakoutAtrBuffer * context.Atr;

        if (volumeOk && context.OpeningRangeHigh is { } openingHigh && context.OpeningRangeLow is { } openingLow)
        {
            if (context.Price > openingHigh + buffer && CrossedRecently(context, openingHigh, true))
            {
                events.Add(Event(context, RadarEventKind.OpeningRangeBreakout, TradeDirection.Buy, BreakoutStrength(context, context.Price - openingHigh), "rompeu o máximo dos primeiros 30 min"));
            }
            else if (context.Price < openingLow - buffer && CrossedRecently(context, openingLow, false))
            {
                events.Add(Event(context, RadarEventKind.OpeningRangeBreakout, TradeDirection.Sell, BreakoutStrength(context, openingLow - context.Price), "rompeu o mínimo dos primeiros 30 min"));
            }
        }

        if (volumeOk && context.PreviousDayHigh is { } previousHigh && context.PreviousDayLow is { } previousLow)
        {
            if (context.Price > previousHigh + buffer && CrossedRecently(context, previousHigh, true))
            {
                events.Add(Event(context, RadarEventKind.PreviousDayBreakout, TradeDirection.Buy, BreakoutStrength(context, context.Price - previousHigh), "rompeu o máximo do dia anterior"));
            }
            else if (context.Price < previousLow - buffer && CrossedRecently(context, previousLow, false))
            {
                events.Add(Event(context, RadarEventKind.PreviousDayBreakout, TradeDirection.Sell, BreakoutStrength(context, previousLow - context.Price), "rompeu o mínimo do dia anterior"));
            }
        }

        if (Math.Abs(context.RecentMove) >= options.BurstAtrMultiple)
        {
            events.Add(Event(
                context,
                RadarEventKind.MomentumBurst,
                context.RecentMove > 0 ? TradeDirection.Buy : TradeDirection.Sell,
                Clamp(Math.Abs(context.RecentMove) / 6, 0, 1),
                $"movimento de {Format(context.RecentMove, "+0.0;-0.0")} ATR nos últimos 30 min"));
        }

        return events;
    }

    public SuggestionDraft? Evaluate(IntradayContext context, SentimentSnapshot sentiment)
    {
        SuggestionDraft? draft = Assess(context, sentiment);
        return draft is { IsShadow: false } ? draft : null;
    }

    public SuggestionDraft? Assess(IntradayContext context, SentimentSnapshot sentiment)
    {
        if (context.Atr <= 0 || context.Price <= 0 || context.RelativeVolume is { } rvol && rvol < options.MinRelativeVolume)
        {
            return null;
        }

        List<SignalResult> signals = BuildSignals(context, sentiment);

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

        double confidence = Math.Min(options.MaxConfidence, Sigmoid(options.ConfidenceSteepness * Math.Abs(score)) * agreeing.Count / signals.Count);
        (double stopLoss, double takeProfit) = ComputeLevels(context, direction);
        double risk = Math.Abs(context.Price - stopLoss);
        double riskReward = risk > 0 ? Math.Abs(takeProfit - context.Price) / risk : 0;

        List<string> blockers = Blockers(context, sign, confidence, riskReward);

        if (blockers.Count > 0 && (!options.RecordShadow || confidence < options.ShadowMinConfidence))
        {
            return null;
        }

        IReadOnlyList<EvidenceArticle> articles = sentiment.Articles
            .Where(a => Math.Sign(a.Sentiment) == sign)
            .OrderByDescending(a => a.Influence)
            .Take(5)
            .ToList();

        return new SuggestionDraft(
            context.Symbol,
            context.Name,
            direction,
            confidence,
            score,
            context.Price,
            stopLoss,
            takeProfit,
            riskReward,
            signals,
            articles,
            BuildExplanation(direction, agreeing, signals.Count),
            blockers);
    }

    private List<string> Blockers(IntradayContext context, int sign, double confidence, double riskReward)
    {
        List<string> blockers = [];

        if (confidence < options.ConfidenceThreshold)
        {
            blockers.Add("certeza abaixo do mínimo");
        }

        if (riskReward < options.MinRiskReward)
        {
            blockers.Add("R:R abaixo do mínimo");
        }

        if (Math.Sign(context.RecentMove) != sign || Math.Abs(context.RecentMove) < options.MinEntryMove)
        {
            blockers.Add("sem movimento recente a favor");
        }

        if (options.MaxRecentMoveAtr > 0 && Math.Abs(context.RecentMove) > options.MaxRecentMoveAtr && Math.Sign(context.RecentMove) == sign)
        {
            blockers.Add("movimento esticado (a perseguir o preço)");
        }

        if (options.MaxDayMovePercent > 0 && context.PreviousDayClose is > 0 and var previousClose
            && Math.Sign(context.Price - previousClose) == sign
            && Math.Abs(context.Price - previousClose) / previousClose * 100 > options.MaxDayMovePercent)
        {
            blockers.Add($"já se moveu mais de {options.MaxDayMovePercent.ToString("0.#", CultureInfo.InvariantCulture)}% no dia");
        }

        if (context.SessionEnd is { } end)
        {
            if (options.AvoidFirstMinutes > 0 && (context.LastCandleTime - context.SessionStart).TotalMinutes < options.AvoidFirstMinutes)
            {
                blockers.Add($"primeiros {options.AvoidFirstMinutes} min da sessão");
            }

            if (options.AvoidLastMinutes > 0 && (end - context.LastCandleTime).TotalMinutes < options.AvoidLastMinutes)
            {
                blockers.Add($"últimos {options.AvoidLastMinutes} min da sessão");
            }
        }

        if (options.RequireMarketAlignment && context.MarketChangePercent is { } market
            && Math.Abs(market) >= options.MarketAlignmentFloorPercent && Math.Sign(market) != sign)
        {
            blockers.Add("contra o sentido do mercado");
        }

        if (options.MaxVix > 0 && context.Vix is { } vix && vix >= options.MaxVix)
        {
            blockers.Add($"VIX elevado ({vix.ToString("0.#", CultureInfo.InvariantCulture)})");
        }

        if (options.MaxVolatilityRatio > 0 && context.VolatilityRatio is { } ratio && ratio >= options.MaxVolatilityRatio)
        {
            blockers.Add($"volatilidade anormal ({ratio.ToString("0.0", CultureInfo.InvariantCulture)}× o normal)");
        }

        if (options.AvoidMacroHours > 0 && context.MacroEvent is { Length: > 0 } macro)
        {
            blockers.Add($"evento macro próximo ({macro})");
        }

        return blockers;
    }

    public (double StopLoss, double TakeProfit) ComputeLevels(IntradayContext context, TradeDirection direction)
    {
        int sign = direction == TradeDirection.Buy ? 1 : -1;
        double stopLoss = context.Price - sign * options.SlAtrMultiple * context.Atr;
        double takeProfit = context.Price + sign * options.TpAtrMultiple * context.Atr;

        if (context.Vwap is { } vwap)
        {
            double distance = sign * (context.Price - vwap) / context.Atr;

            if (distance is >= 0.5 and <= 2.2)
            {
                double vwapStop = vwap - sign * 0.2 * context.Atr;

                if (Math.Abs(context.Price - vwapStop) >= options.MinStopAtrMultiple * context.Atr)
                {
                    stopLoss = vwapStop;
                }
            }
        }

        return (stopLoss, takeProfit);
    }

    private List<SignalResult> BuildSignals(IntradayContext context, SentimentSnapshot sentiment)
    {
        List<SignalResult> signals = [];

        if (context.Vwap is { } vwap)
        {
            double percent = (context.Price - vwap) / vwap * 100;
            signals.Add(new SignalResult(
                "vwap",
                "VWAP",
                Clamp((context.Price - vwap) / (1.5 * context.Atr)),
                options.VwapWeight,
                $"preço {Format(Math.Abs(percent), "0.00")}% {(percent >= 0 ? "acima" : "abaixo")} do VWAP"));
        }

        if (OpeningRangeSignal(context) is { } openingRange)
        {
            signals.Add(openingRange);
        }

        signals.Add(new SignalResult(
            "intraday-momentum",
            "Momentum intradiário",
            Clamp(context.RecentMove / 3),
            options.MomentumWeight,
            $"movimento de {Format(context.RecentMove, "+0.0;-0.0")} ATR nos últimos 30 min"));

        if (context.RelativeVolume is { } rvol && rvol >= options.MinRelativeVolume)
        {
            int direction = context.RecentMove != 0 ? Math.Sign(context.RecentMove) : Math.Sign(context.Price - context.DayOpen);
            signals.Add(new SignalResult(
                "relative-volume",
                "Volume relativo",
                direction * Clamp((rvol - 1) / 2, 0, 1),
                options.VolumeWeight,
                $"volume {Format(rvol, "0.0")}× acima do normal para esta hora"));
        }

        if (PreviousDaySignal(context) is { } previousDay)
        {
            signals.Add(previousDay);
        }

        if (GapSignal(context) is { } gap)
        {
            signals.Add(gap);
        }

        if (sentiment.ArticleCount >= 1)
        {
            signals.Add(new SignalResult(
                "recent-news",
                "Notícias recentes",
                Clamp(sentiment.WeightedSentiment / 0.5),
                options.NewsWeight,
                $"sentimento {Format(sentiment.WeightedSentiment, "+0.00;-0.00")} em {sentiment.ArticleCount} artigos das últimas {options.NewsWindowHours:0} h"));
        }

        if (CatalystSignal(context, sentiment) is { } catalyst)
        {
            signals.Add(catalyst);
        }

        if (context.MarketChangePercent is { } market && options.MarketWeight > 0)
        {
            signals.Add(new SignalResult(
                "market",
                "Mercado geral",
                Clamp(market / 1.0),
                options.MarketWeight,
                $"o mercado de referência está {Format(market, "+0.0;-0.0")}% hoje"));
        }

        return signals;
    }

    private SignalResult? OpeningRangeSignal(IntradayContext context)
    {
        if (context.OpeningRangeHigh is not { } high || context.OpeningRangeLow is not { } low || high <= low)
        {
            return null;
        }

        double value;
        string detail;

        if (context.Price > high)
        {
            value = 1;
            detail = "acima do máximo dos primeiros 30 min";
        }
        else if (context.Price < low)
        {
            value = -1;
            detail = "abaixo do mínimo dos primeiros 30 min";
        }
        else
        {
            double middle = (high + low) / 2;
            value = 0.4 * Clamp((context.Price - middle) / ((high - low) / 2));
            detail = "dentro da amplitude dos primeiros 30 min";
        }

        return new SignalResult("opening-range", "Abertura (30 min)", value, options.OpeningRangeWeight, detail);
    }

    private SignalResult? PreviousDaySignal(IntradayContext context)
    {
        if (context.PreviousDayHigh is { } high && context.Price > high)
        {
            return new SignalResult("previous-day", "Dia anterior", 1, options.PreviousDayWeight, "acima do máximo do dia anterior");
        }

        if (context.PreviousDayLow is { } low && context.Price < low)
        {
            return new SignalResult("previous-day", "Dia anterior", -1, options.PreviousDayWeight, "abaixo do mínimo do dia anterior");
        }

        return null;
    }

    private SignalResult? GapSignal(IntradayContext context)
    {
        if (context.GapPercent is not { } gap || Math.Abs(gap) < 0.5)
        {
            return null;
        }

        int direction = Math.Sign(gap);
        bool holding = Math.Sign(context.Price - context.DayOpen) == direction;
        double value = holding ? direction * Clamp(Math.Abs(gap) / 3, 0, 1) : -direction * 0.3;

        return new SignalResult(
            "gap",
            "Gap de abertura",
            value,
            options.GapWeight,
            $"gap de {Format(gap, "+0.0;-0.0")}% {(holding ? "a manter-se" : "a ser revertido")}");
    }

    private SignalResult? CatalystSignal(IntradayContext context, SentimentSnapshot sentiment)
    {
        if (context.RelativeVolume is { } rvol && rvol < options.MinRelativeVolume)
        {
            return null;
        }

        EvidenceArticle? article = sentiment.Articles
            .Where(a => Math.Abs(a.Sentiment) >= options.CatalystMinSentiment
                && a.PublishedAt <= context.LastCandleTime.AddMinutes(5)
                && context.LastCandleTime - a.PublishedAt <= TimeSpan.FromMinutes(options.CatalystWindowMinutes))
            .OrderByDescending(a => a.Influence)
            .FirstOrDefault();

        Candle? before = article is null ? null : context.SessionCandles.LastOrDefault(c => c.Time <= article.PublishedAt);

        if (article is null || before is null)
        {
            return null;
        }

        double reaction = (context.Price - before.Close) / context.Atr;
        int sign = Math.Sign(article.Sentiment);

        if (Math.Sign(reaction) != sign || Math.Abs(reaction) < 0.5)
        {
            return null;
        }

        int minutes = Math.Max(0, (int)(context.LastCandleTime - article.PublishedAt).TotalMinutes);

        return new SignalResult(
            "catalyst",
            "Notícia + reação do preço",
            sign * Clamp(Math.Abs(reaction) / 2, 0, 1),
            options.CatalystWeight,
            $"notícia há {minutes} min ({Format(article.Sentiment, "+0.00;-0.00")}) e o preço reagiu {Format(reaction, "+0.0;-0.0")} ATR");
    }

    private bool CrossedRecently(IntradayContext context, double level, bool upward) =>
        context.SessionCandles.TakeLast(options.BreakoutFreshCandles).Any(c => upward ? c.Close <= level : c.Close >= level);

    private static RadarEvent Event(IntradayContext context, RadarEventKind kind, TradeDirection direction, double strength, string detail) =>
        new(context.Symbol, context.Name, kind, direction, strength, detail, context.Price, context.LastCandleTime);

    private static double BreakoutStrength(IntradayContext context, double distance) =>
        Clamp(0.3 + 0.05 * distance / context.Atr + 0.1 * Math.Max(0, (context.RelativeVolume ?? 1) - 1), 0, 1);

    private static string BuildExplanation(TradeDirection direction, List<SignalResult> agreeing, int total)
    {
        string verb = direction == TradeDirection.Buy ? "Compra" : "Venda";
        string reasons = string.Join("; ", agreeing.OrderByDescending(s => s.Weight * Math.Abs(s.Value)).Select(s => s.Detail));

        return $"{verb} intradiária sugerida: {reasons}. {agreeing.Count} de {total} sinais concordam.";
    }

    private static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));

    private static double Clamp(double value, double min = -1, double max = 1) => Math.Min(max, Math.Max(min, value));

    private static string Format(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
