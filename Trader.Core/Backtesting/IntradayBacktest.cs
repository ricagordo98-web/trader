using Trader.Core.Intraday;
using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Core.Backtesting;

public sealed class IntradayBacktestRequest
{
    public int Days { get; set; } = 20;
    public AssetClass? AssetClass { get; set; }
    public int MaxSymbols { get; set; } = 30;
    public double SlippagePercent { get; set; } = 0.02;
    public int StepCandles { get; set; } = 3;
    public CostModel Costs { get; set; } = new();
}

public sealed record IntradayHistory(string Symbol, string Name, AssetClass AssetClass, IReadOnlyList<Candle> Candles);

public sealed record IntradayBacktestResult(
    DateTimeOffset GeneratedAt,
    DateTimeOffset From,
    DateTimeOffset To,
    int SymbolsTested,
    int SessionsTested,
    IntradayBacktestRequest Request,
    BacktestStats Strategy,
    BacktestStats Mirrored,
    BacktestStats FirstHalf,
    BacktestStats SecondHalf,
    double AverageMinutesHeld,
    IReadOnlyList<BacktestGroup> ByHour,
    IReadOnlyList<BacktestGroup> ByAssetClass,
    IReadOnlyList<BacktestGroup> BySignal,
    IReadOnlyList<CurvePoint> Curve,
    BacktestVerdict Verdict,
    IReadOnlyList<string> Notes);

public static class IntradayBacktestRunner
{
    public const int MaxDays = 25;
    public const int MaxSymbolsAllowed = 60;
    private const int MinSessionCandles = 20;
    private const int WarmupCandles = 6;
    private const int EndBuffer = 6;
    private const int HistoryDays = 5;

    public static IReadOnlyList<string> Validate(IntradayBacktestRequest request)
    {
        List<string> errors = [];

        if (request.Days is < 3 or > MaxDays)
        {
            errors.Add($"Os dias têm de estar entre 3 e {MaxDays} (o Yahoo só guarda cerca de 1 mês de velas de 5 minutos).");
        }

        if (request.MaxSymbols is < 1 or > MaxSymbolsAllowed)
        {
            errors.Add($"O número de ativos tem de estar entre 1 e {MaxSymbolsAllowed}.");
        }

        if (request.SlippagePercent is < 0 or > 1)
        {
            errors.Add("O slippage tem de estar entre 0% e 1%.");
        }

        if (request.StepCandles is < 1 or > 12)
        {
            errors.Add("O intervalo entre avaliações tem de estar entre 1 e 12 velas.");
        }

        return errors;
    }

    public static IntradayBacktestResult Run(IReadOnlyList<IntradayHistory> histories, IntradayOptions options, IntradayBacktestRequest request, DateTimeOffset now)
    {
        IntradayEngine engine = new(options);
        List<SimulatedTrade> trades = [];
        int sessions = 0;
        DateTimeOffset cutoff = now.AddDays(-request.Days);

        foreach (IntradayHistory history in histories)
        {
            (List<SimulatedTrade> found, int tested) = Simulate(history, engine, options, request, cutoff);
            trades.AddRange(found);
            sessions += tested;
        }

        List<BacktestTrade> real = trades.Select(t => t.Real).ToList();
        List<BacktestTrade> mirrored = trades.Select(t => t.Mirrored).ToList();
        BacktestStats strategy = BacktestStatistics.Calculate(real);
        BacktestStats baseline = BacktestStatistics.Calculate(mirrored);
        List<BacktestTrade> ordered = real.OrderBy(t => t.ExitTime).ToList();
        int half = ordered.Count / 2;
        BacktestStats first = BacktestStatistics.Calculate(ordered.Take(half).ToList());
        BacktestStats second = BacktestStatistics.Calculate(ordered.Skip(half).ToList());

        List<string> notes =
        [
            "Sem notícias: o histórico de artigos não existe para estes dias, por isso só entram sinais técnicos (VWAP, abertura, volume, momentum, dia anterior, gap).",
            "A entrada é na abertura da vela seguinte ao sinal, para não usar informação do futuro. Dentro da mesma vela, o stop conta antes do alvo.",
            $"Custos: spread da classe de ativo + {request.SlippagePercent.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)}% de slippage por lado.",
            "A direção contrária usa as mesmas entradas com stop e alvo espelhados: se a estratégia não a supera, o sinal não acrescenta nada."
        ];

        if (histories.Count > 0 && sessions == 0)
        {
            notes.Add("Nenhuma sessão completa encontrada: os mercados podem estar fechados ou sem dados de 5 minutos.");
        }

        return new IntradayBacktestResult(
            now,
            trades.Count == 0 ? cutoff : trades.Min(t => t.Real.EntryTime),
            now,
            histories.Count,
            sessions,
            request,
            strategy,
            baseline,
            first,
            second,
            trades.Count == 0 ? 0 : trades.Average(t => t.Minutes),
            Groups(trades, t => $"{t.Real.EntryTime.UtcDateTime.Hour:00}h UTC").OrderBy(g => g.Name).ToList(),
            Groups(trades, t => t.Real.AssetClass.ToString()).ToList(),
            SignalGroups(trades),
            BacktestStatistics.EquityCurve(real),
            BacktestStatistics.Evaluate(strategy, baseline, first, second),
            notes);
    }

    private static IReadOnlyList<BacktestGroup> Groups(List<SimulatedTrade> trades, Func<SimulatedTrade, string> key) =>
        trades.GroupBy(key)
            .Select(g => new BacktestGroup(g.Key, BacktestStatistics.Calculate(g.Select(t => t.Real).ToList())))
            .OrderByDescending(g => g.Stats.Trades)
            .ToList();

    private static IReadOnlyList<BacktestGroup> SignalGroups(List<SimulatedTrade> trades) =>
        trades.SelectMany(t => t.Signals.Select(s => (Signal: s, Trade: t.Real)))
            .GroupBy(x => x.Signal)
            .Select(g => new BacktestGroup(g.Key, BacktestStatistics.Calculate(g.Select(x => x.Trade).ToList())))
            .OrderByDescending(g => g.Stats.Trades)
            .ToList();

    private static (List<SimulatedTrade> Trades, int Sessions) Simulate(IntradayHistory history, IntradayEngine engine, IntradayOptions options, IntradayBacktestRequest request, DateTimeOffset cutoff)
    {
        List<Candle> candles = history.Candles.Where(c => c.Close > 0).OrderBy(c => c.Time).ToList();
        List<List<Candle>> days = candles.GroupBy(c => c.Time.UtcDateTime.Date).Select(g => g.ToList()).ToList();
        List<SimulatedTrade> trades = [];
        int sessions = 0;
        SentimentSnapshot noNews = new(0, 0, null, []);

        for (int d = 1; d < days.Count; d++)
        {
            List<Candle> session = days[d];

            if (session.Count < MinSessionCandles || session[0].Time < cutoff)
            {
                continue;
            }

            sessions++;
            List<Candle> context = days.Skip(Math.Max(0, d - HistoryDays)).Take(Math.Min(d, HistoryDays) + 1).SelectMany(x => x).ToList();
            int sessionStart = context.Count - session.Count;
            int index = WarmupCandles;

            while (index < session.Count - EndBuffer)
            {
                int absolute = sessionStart + index;
                List<Candle> visible = context.Take(absolute + 1).ToList();
                IntradayContext? snapshot = IntradayAnalyzer.Analyze(history.Symbol, history.Name, history.AssetClass, visible, visible[^1].Time.AddMinutes(1), options);
                SuggestionDraft? draft = snapshot is null ? null : engine.Evaluate(snapshot, noNews);

                if (draft is null)
                {
                    index += request.StepCandles;
                    continue;
                }

                (SimulatedTrade? trade, int exitIndex) = Trade(history, draft, session, index, request, options);

                if (trade is null)
                {
                    index += request.StepCandles;
                    continue;
                }

                trades.Add(trade);
                index = exitIndex + 1;
            }
        }

        return (trades, sessions);
    }

    private static (SimulatedTrade? Trade, int ExitIndex) Trade(IntradayHistory history, SuggestionDraft draft, List<Candle> session, int signalIndex, IntradayBacktestRequest request, IntradayOptions options)
    {
        int entryIndex = signalIndex + 1;

        if (entryIndex >= session.Count)
        {
            return (null, signalIndex);
        }

        Candle entryCandle = session[entryIndex];
        double entry = entryCandle.Open;
        double stopDistance = Math.Abs(draft.Entry - draft.StopLoss);
        double targetDistance = Math.Abs(draft.TakeProfit - draft.Entry);
        int sign = draft.Direction == TradeDirection.Buy ? 1 : -1;

        if (entry <= 0 || stopDistance <= 0)
        {
            return (null, signalIndex);
        }

        int horizon = Math.Min(session.Count - 1, entryIndex + options.ExpiryHours * 12);
        (double exit, int exitIndex, SuggestionStatus outcome) = Walk(session, entryIndex, horizon, entry, sign, stopDistance, targetDistance);
        (double mirroredExit, _, SuggestionStatus mirroredOutcome) = Walk(session, entryIndex, horizon, entry, -sign, stopDistance, targetDistance);

        double minutes = Math.Max(5, (session[exitIndex].Time - entryCandle.Time).TotalMinutes + 5);
        double costR = (request.Costs.RoundTripPercent(history.AssetClass) + request.SlippagePercent * 2) / 100 * entry / stopDistance;

        BacktestTrade real = Build(history, draft.Direction, entryCandle.Time, session[exitIndex].Time, entry, exit, outcome, minutes, sign, stopDistance, costR);
        BacktestTrade mirrored = Build(history, draft.Direction == TradeDirection.Buy ? TradeDirection.Sell : TradeDirection.Buy, entryCandle.Time, session[exitIndex].Time, entry, mirroredExit, mirroredOutcome, minutes, -sign, stopDistance, costR);
        string[] signals = draft.Signals.Where(s => Math.Sign(s.Value) == sign && Math.Abs(s.Value) >= 0.1).Select(s => s.Name).ToArray();

        return (new SimulatedTrade(real, mirrored, minutes, signals), exitIndex);
    }

    private static (double Exit, int Index, SuggestionStatus Outcome) Walk(List<Candle> session, int entryIndex, int horizon, double entry, int sign, double stopDistance, double targetDistance)
    {
        double stop = entry - sign * stopDistance;
        double target = entry + sign * targetDistance;

        for (int i = entryIndex; i <= horizon; i++)
        {
            Candle candle = session[i];
            bool hitStop = sign == 1 ? candle.Low <= stop : candle.High >= stop;
            bool hitTarget = sign == 1 ? candle.High >= target : candle.Low <= target;

            if (hitStop)
            {
                return (stop, i, SuggestionStatus.HitStopLoss);
            }

            if (hitTarget)
            {
                return (target, i, SuggestionStatus.HitTakeProfit);
            }
        }

        return (session[horizon].Close, horizon, SuggestionStatus.Expired);
    }

    private static BacktestTrade Build(IntradayHistory history, TradeDirection direction, DateTimeOffset entryTime, DateTimeOffset exitTime, double entry, double exit, SuggestionStatus outcome, double minutes, int sign, double stopDistance, double costR)
    {
        double gross = sign * (exit - entry) / stopDistance;
        double net = gross - costR;

        return new BacktestTrade(
            history.Symbol,
            history.AssetClass,
            direction,
            entryTime,
            exitTime,
            entry,
            exit,
            outcome,
            minutes / 1440,
            gross,
            net,
            net * stopDistance / entry * 100);
    }

    private sealed record SimulatedTrade(BacktestTrade Real, BacktestTrade Mirrored, double Minutes, string[] Signals);
}
