using Trader.Core.Models;

namespace Trader.Core.Explain;

public sealed record BenchmarkInput(string Symbol, string Name, IReadOnlyList<Candle> Candles, double Beta);

public static class MoveAnalyzer
{
    private const int MinHistoryReturns = 10;
    private const int MaxVolumeHistory = 200;
    private const double SmallChangePercent = 0.3;
    private const double SmallZScore = 1.25;
    private const double MarketShareThreshold = 0.6;
    private const double ResidualShareThreshold = 0.6;
    private const double PeerMoveShare = 0.5;
    private const double PeerCorrelation = 0.6;
    private const int MinSectorPeers = 2;
    private const double ReportedGapPercent = 0.5;

    public static MoveAnalysis? Analyze(
        string symbol,
        string name,
        string interval,
        IReadOnlyList<Candle> series,
        DateTimeOffset from,
        DateTimeOffset to,
        BenchmarkInput? benchmark,
        IReadOnlyList<PeerMove> peers)
    {
        List<Candle> ordered = series.Where(c => c.Close > 0).OrderBy(c => c.Time).ToList();
        List<Candle> window = ordered.Where(c => c.Time >= from && c.Time <= to).ToList();

        if (window.Count == 0)
        {
            return null;
        }

        int firstIndex = ordered.IndexOf(window[0]);
        double start = firstIndex > 0 ? ordered[firstIndex - 1].Close : window[0].Open;

        if (start <= 0)
        {
            return null;
        }

        double end = window[^1].Close;
        double change = (end - start) / start * 100;

        double typical = TypicalMove(ordered, firstIndex, window.Count);
        double z = typical > 0 ? change / typical : 0;
        (DateTimeOffset peakTime, double peakChange) = Peak(ordered, firstIndex, window);
        double share = Math.Abs(change) > 1e-9 && Math.Sign(peakChange) == Math.Sign(change) ? Math.Min(1, peakChange / change) : 0;

        double? gap = firstIndex > 0 ? (window[0].Open - start) / start * 100 : null;
        BenchmarkMove? benchmarkMove = BuildBenchmark(benchmark, from, to);
        double residual = benchmarkMove is null ? change : change - benchmarkMove.ExpectedPercent;
        MoveScope scope = Classify(change, z, benchmarkMove, residual, peers);

        return new MoveAnalysis(
            symbol,
            name,
            from,
            to,
            interval,
            start,
            end,
            change,
            typical,
            z,
            peakTime,
            peakChange,
            share,
            VolumeRatio(ordered, firstIndex, window),
            gap is { } g && Math.Abs(g) >= ReportedGapPercent ? g : null,
            benchmarkMove,
            residual,
            peers,
            scope);
    }

    public static double? ChangeOver(IReadOnlyList<Candle> series, DateTimeOffset from, DateTimeOffset to)
    {
        List<Candle> ordered = series.Where(c => c.Close > 0).OrderBy(c => c.Time).ToList();
        List<Candle> window = ordered.Where(c => c.Time >= from && c.Time <= to).ToList();

        if (window.Count == 0)
        {
            return null;
        }

        int firstIndex = ordered.IndexOf(window[0]);
        double start = firstIndex > 0 ? ordered[firstIndex - 1].Close : window[0].Open;

        return start > 0 ? (window[^1].Close - start) / start * 100 : null;
    }

    private static double TypicalMove(List<Candle> ordered, int firstIndex, int windowCount)
    {
        List<double> returns = [];

        for (int i = 1; i < firstIndex; i++)
        {
            returns.Add((ordered[i].Close - ordered[i - 1].Close) / ordered[i - 1].Close * 100);
        }

        if (returns.Count < MinHistoryReturns)
        {
            returns.Clear();

            for (int i = 1; i < ordered.Count; i++)
            {
                returns.Add((ordered[i].Close - ordered[i - 1].Close) / ordered[i - 1].Close * 100);
            }
        }

        if (returns.Count < 2)
        {
            return 0;
        }

        double mean = returns.Average();
        double deviation = Math.Sqrt(returns.Sum(r => Math.Pow(r - mean, 2)) / (returns.Count - 1));
        return deviation * Math.Sqrt(windowCount);
    }

    private static (DateTimeOffset Time, double Change) Peak(List<Candle> ordered, int firstIndex, List<Candle> window)
    {
        DateTimeOffset peakTime = window[0].Time;
        double peak = 0;

        for (int i = 0; i < window.Count; i++)
        {
            double previous = firstIndex + i > 0 ? ordered[firstIndex + i - 1].Close : window[i].Open;
            double move = previous > 0 ? (window[i].Close - previous) / previous * 100 : 0;

            if (Math.Abs(move) > Math.Abs(peak))
            {
                peak = move;
                peakTime = window[i].Time;
            }
        }

        return (peakTime, peak);
    }

    private static double? VolumeRatio(List<Candle> ordered, int firstIndex, List<Candle> window)
    {
        List<double> recent = window.Where(c => c.Volume > 0).Select(c => c.Volume).ToList();
        List<double> history = ordered.Take(firstIndex).Where(c => c.Volume > 0).TakeLast(MaxVolumeHistory).Select(c => c.Volume).ToList();

        return recent.Count > 0 && history.Count >= MinHistoryReturns ? recent.Average() / history.Average() : null;
    }

    private static BenchmarkMove? BuildBenchmark(BenchmarkInput? benchmark, DateTimeOffset from, DateTimeOffset to)
    {
        if (benchmark is null || ChangeOver(benchmark.Candles, from, to) is not { } change)
        {
            return null;
        }

        return new BenchmarkMove(benchmark.Symbol, benchmark.Name, change, benchmark.Beta, benchmark.Beta * change);
    }

    private static MoveScope Classify(double change, double z, BenchmarkMove? benchmark, double residual, IReadOnlyList<PeerMove> peers)
    {
        if (Math.Abs(change) < SmallChangePercent || Math.Abs(z) < SmallZScore)
        {
            return MoveScope.Small;
        }

        if (benchmark is not null && Math.Sign(benchmark.ExpectedPercent) == Math.Sign(change) && Math.Abs(benchmark.ExpectedPercent) >= MarketShareThreshold * Math.Abs(change))
        {
            return MoveScope.MarketWide;
        }

        int sameDirectionPeers = peers.Count(p => Math.Abs(p.Correlation) >= PeerCorrelation
            && Math.Sign(p.ChangePercent) == Math.Sign(change)
            && Math.Abs(p.ChangePercent) >= PeerMoveShare * Math.Abs(change));

        if (sameDirectionPeers >= MinSectorPeers)
        {
            return MoveScope.Sector;
        }

        return Math.Abs(residual) >= ResidualShareThreshold * Math.Abs(change) ? MoveScope.CompanySpecific : MoveScope.Mixed;
    }
}
