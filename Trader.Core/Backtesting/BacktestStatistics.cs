using Trader.Core.Models;

namespace Trader.Core.Backtesting;

public static class BacktestStatistics
{
    public const int MinimumTrades = 30;

    private const double ConfidenceZ = 1.96;

    public static BacktestStats Calculate(IReadOnlyList<BacktestTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new BacktestStats(0, 0, 0, 0, 0, 0, null, 0, 0, 0, 0, 0);
        }

        List<BacktestTrade> ordered = trades.OrderBy(t => t.ExitTime).ToList();
        double[] net = ordered.Select(t => t.NetR).ToArray();
        double mean = net.Average();
        double stdDev = net.Length > 1 ? Math.Sqrt(net.Sum(r => Math.Pow(r - mean, 2)) / (net.Length - 1)) : 0;
        double margin = net.Length > 1 ? ConfidenceZ * stdDev / Math.Sqrt(net.Length) : 0;

        double grossProfit = net.Where(r => r > 0).Sum();
        double grossLoss = Math.Abs(net.Where(r => r < 0).Sum());

        double peak = 0;
        double running = 0;
        double maxDrawdown = 0;

        foreach (double r in net)
        {
            running += r;
            peak = Math.Max(peak, running);
            maxDrawdown = Math.Max(maxDrawdown, peak - running);
        }

        int wins = net.Count(r => r > 0);

        return new BacktestStats(
            net.Length,
            wins,
            wins / (double)net.Length,
            ordered.Average(t => t.GrossR),
            mean,
            net.Sum(),
            grossLoss > 0 ? grossProfit / grossLoss : null,
            maxDrawdown,
            ordered.Average(t => t.NetPercent),
            stdDev,
            mean - margin,
            mean + margin);
    }

    public static IReadOnlyList<CurvePoint> EquityCurve(IReadOnlyList<BacktestTrade> trades)
    {
        double running = 0;

        return trades
            .OrderBy(t => t.ExitTime)
            .Select(t =>
            {
                running += t.NetR;
                return new CurvePoint(t.ExitTime, running);
            })
            .ToList();
    }

    public static BacktestVerdict Evaluate(BacktestStats strategy, BacktestStats baseline, BacktestStats firstHalf, BacktestStats secondHalf)
    {
        if (strategy.Trades < MinimumTrades)
        {
            return new BacktestVerdict(
                "insufficient",
                "Amostra insuficiente",
                $"Só {strategy.Trades} operações (mínimo {MinimumTrades}). Com tão poucas, o resultado pode ser pura sorte e não permite concluir nada.");
        }

        if (strategy.AverageNetR <= 0)
        {
            return new BacktestVerdict(
                "no-edge",
                "Sem margem após custos",
                $"O R médio líquido é {strategy.AverageNetR:+0.000;-0.000}. Neste histórico, seguir estas sugestões teria perdido dinheiro depois de custos.");
        }

        bool significant = strategy.ConfidenceLow > 0;
        bool beatsBaseline = strategy.AverageNetR > baseline.AverageNetR;
        bool stable = firstHalf.Trades >= 10 && secondHalf.Trades >= 10 && firstHalf.AverageNetR > 0 && secondHalf.AverageNetR > 0;

        if (significant && beatsBaseline && stable)
        {
            return new BacktestVerdict(
                "promising",
                "Indícios de margem",
                "O R médio líquido é positivo com intervalo de confiança acima de zero, supera a direção aleatória e mantém-se nas duas metades do período. Ainda assim é histórico: confirma com o Track record em tempo real antes de arriscares dinheiro a sério.");
        }

        return new BacktestVerdict(
            "inconclusive",
            "Inconclusivo",
            $"O R médio líquido é positivo ({strategy.AverageNetR:+0.000;-0.000}), mas {Reasons(significant, beatsBaseline, stable)}. Não dá para distinguir de sorte.");
    }

    private static string Reasons(bool significant, bool beatsBaseline, bool stable)
    {
        List<string> reasons = [];

        if (!significant)
        {
            reasons.Add("o intervalo de confiança inclui zero");
        }

        if (!beatsBaseline)
        {
            reasons.Add("não supera a direção aleatória nos mesmos dias");
        }

        if (!stable)
        {
            reasons.Add("o resultado não se repete nas duas metades do período");
        }

        return string.Join("; ", reasons);
    }
}
