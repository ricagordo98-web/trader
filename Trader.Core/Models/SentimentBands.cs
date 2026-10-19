namespace Trader.Core.Models;

public static class SentimentBands
{
    public const double Threshold = 0.2;

    public static bool IsPositive(double? score) => score > Threshold;

    public static bool IsNegative(double? score) => score < -Threshold;
}
