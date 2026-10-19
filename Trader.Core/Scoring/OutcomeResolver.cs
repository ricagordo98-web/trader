using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed record OutcomeResult(
    SuggestionStatus Status,
    double ExitPrice,
    DateTimeOffset ClosedAt,
    double RMultiple);

public static class OutcomeResolver
{
    public static OutcomeResult? Resolve(Suggestion suggestion, IReadOnlyList<Candle> candles, DateTimeOffset now)
    {
        double risk = Math.Abs(suggestion.Entry - suggestion.StopLoss);

        if (risk <= 0)
        {
            return null;
        }

        int sign = suggestion.Direction == TradeDirection.Buy ? 1 : -1;
        Candle? last = null;

        foreach (Candle candle in candles.Where(c => c.Time > suggestion.CreatedAt && c.Time <= suggestion.ExpiresAt).OrderBy(c => c.Time))
        {
            last = candle;

            bool hitStop = sign == 1 ? candle.Low <= suggestion.StopLoss : candle.High >= suggestion.StopLoss;
            bool hitTarget = sign == 1 ? candle.High >= suggestion.TakeProfit : candle.Low <= suggestion.TakeProfit;

            if (hitStop)
            {
                return new OutcomeResult(SuggestionStatus.HitStopLoss, suggestion.StopLoss, candle.Time, -1);
            }

            if (hitTarget)
            {
                return new OutcomeResult(
                    SuggestionStatus.HitTakeProfit,
                    suggestion.TakeProfit,
                    candle.Time,
                    Math.Abs(suggestion.TakeProfit - suggestion.Entry) / risk);
            }
        }

        if (now < suggestion.ExpiresAt || last is null)
        {
            return null;
        }

        double rMultiple = sign * (last.Close - suggestion.Entry) / risk;
        return new OutcomeResult(SuggestionStatus.Expired, last.Close, suggestion.ExpiresAt, rMultiple);
    }
}
