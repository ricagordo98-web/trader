using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed record ExitPolicy(
    bool UseSmartExit = false,
    double PartialAtR = 1.0,
    double PartialFraction = 0.5,
    double BreakevenAtR = 1.0,
    double LockR = 0.1,
    double TrailAfterR = 1.5,
    double TrailDistanceR = 1.0);

public sealed record OutcomeResult(
    SuggestionStatus Status,
    double ExitPrice,
    DateTimeOffset ClosedAt,
    double RMultiple,
    double FixedR = 0,
    double SmartR = 0,
    double MfeR = 0,
    double MaeR = 0);

public static class OutcomeResolver
{
    public static OutcomeResult? Resolve(Suggestion suggestion, IReadOnlyList<Candle> candles, DateTimeOffset now, ExitPolicy? policy = null)
    {
        double risk = Math.Abs(suggestion.Entry - suggestion.StopLoss);

        if (risk <= 0)
        {
            return null;
        }

        policy ??= new ExitPolicy();
        int sign = suggestion.Direction == TradeDirection.Buy ? 1 : -1;
        double targetR = Math.Abs(suggestion.TakeProfit - suggestion.Entry) / risk;
        List<Candle> path = candles.Where(c => c.Time > suggestion.CreatedAt && c.Time <= suggestion.ExpiresAt).OrderBy(c => c.Time).ToList();

        SuggestionStatus? status = null;
        double fixedR = 0;
        double exitPrice = 0;
        DateTimeOffset closedAt = default;
        int resolvedAt = -1;

        for (int i = 0; i < path.Count; i++)
        {
            Candle candle = path[i];
            bool hitStop = sign == 1 ? candle.Low <= suggestion.StopLoss : candle.High >= suggestion.StopLoss;
            bool hitTarget = sign == 1 ? candle.High >= suggestion.TakeProfit : candle.Low <= suggestion.TakeProfit;

            if (hitStop)
            {
                (status, fixedR, exitPrice, closedAt, resolvedAt) = (SuggestionStatus.HitStopLoss, -1, suggestion.StopLoss, candle.Time, i);
                break;
            }

            if (hitTarget)
            {
                (status, fixedR, exitPrice, closedAt, resolvedAt) = (SuggestionStatus.HitTakeProfit, targetR, suggestion.TakeProfit, candle.Time, i);
                break;
            }
        }

        if (status is null)
        {
            if (now < suggestion.ExpiresAt || path.Count == 0)
            {
                return null;
            }

            Candle last = path[^1];
            (status, fixedR, exitPrice, closedAt, resolvedAt) = (SuggestionStatus.Expired, sign * (last.Close - suggestion.Entry) / risk, last.Close, suggestion.ExpiresAt, path.Count - 1);
        }

        (double mfe, double mae) = Excursions(suggestion, path, resolvedAt, sign, risk);
        double smartR = SmartExit(suggestion, path, resolvedAt, sign, risk, targetR, policy);

        return new OutcomeResult(status.Value, exitPrice, closedAt, policy.UseSmartExit ? smartR : fixedR, fixedR, smartR, mfe, mae);
    }

    public static (double MfeR, double MaeR) Excursions(Suggestion suggestion, IReadOnlyList<Candle> path, int lastIndex, int sign, double risk)
    {
        double mfe = 0;
        double mae = 0;

        for (int i = 0; i <= lastIndex && i < path.Count; i++)
        {
            double favourable = sign == 1 ? path[i].High - suggestion.Entry : suggestion.Entry - path[i].Low;
            double adverse = sign == 1 ? suggestion.Entry - path[i].Low : path[i].High - suggestion.Entry;
            mfe = Math.Max(mfe, favourable / risk);
            mae = Math.Max(mae, adverse / risk);
        }

        return (mfe, mae);
    }

    private static double SmartExit(Suggestion suggestion, IReadOnlyList<Candle> path, int fixedEndIndex, int sign, double risk, double targetR, ExitPolicy policy)
    {
        double stop = suggestion.StopLoss;
        double remaining = 1;
        double realized = 0;
        double peakR = 0;
        bool partialDone = false;
        int end = Math.Min(path.Count - 1, Math.Max(fixedEndIndex, 0));

        for (int i = 0; i <= end; i++)
        {
            Candle candle = path[i];
            bool hitStop = sign == 1 ? candle.Low <= stop : candle.High >= stop;

            if (hitStop)
            {
                return realized + remaining * (sign * (stop - suggestion.Entry) / risk);
            }

            bool hitTarget = sign == 1 ? candle.High >= suggestion.TakeProfit : candle.Low <= suggestion.TakeProfit;

            if (hitTarget)
            {
                return realized + remaining * targetR;
            }

            peakR = Math.Max(peakR, (sign == 1 ? candle.High - suggestion.Entry : suggestion.Entry - candle.Low) / risk);

            if (!partialDone && policy.PartialFraction > 0 && peakR >= policy.PartialAtR)
            {
                realized += policy.PartialFraction * policy.PartialAtR;
                remaining -= policy.PartialFraction;
                partialDone = true;
            }

            if (policy.BreakevenAtR > 0 && peakR >= policy.BreakevenAtR)
            {
                stop = sign == 1 ? Math.Max(stop, suggestion.Entry + policy.LockR * risk) : Math.Min(stop, suggestion.Entry - policy.LockR * risk);
            }

            if (policy.TrailAfterR > 0 && peakR >= policy.TrailAfterR)
            {
                double trail = suggestion.Entry + sign * (peakR - policy.TrailDistanceR) * risk;
                stop = sign == 1 ? Math.Max(stop, trail) : Math.Min(stop, trail);
            }
        }

        Candle final = path[end];
        return realized + remaining * (sign * (final.Close - suggestion.Entry) / risk);
    }
}
