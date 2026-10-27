using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed record LearnedWeight(string Signal, string Label, double CurrentWeight, double ProposedWeight, int Samples, double Lift);

public sealed record WeightLearning(
    SuggestionHorizon Horizon,
    int Samples,
    int TrainSamples,
    int TestSamples,
    double? TestAucCurrent,
    double? TestAucProposed,
    bool Validated,
    string Message,
    IReadOnlyList<LearnedWeight> Weights);

public static class WeightLearner
{
    public const int MinSamples = 150;
    public const int MinTestSamples = 30;
    public const double AucMargin = 0.02;

    private const double AgreementFloor = 0.1;
    private const double Gain = 2;
    private const double ShrinkSamples = 20;
    private const double MinFactor = 0.5;
    private const double MaxFactor = 1.5;
    private const double TrainShare = 0.7;

    public static WeightLearning Learn(SuggestionHorizon horizon, IReadOnlyList<SuggestionDto> closed, IReadOnlyDictionary<string, double> currentWeights)
    {
        List<SuggestionDto> items = closed
            .Where(s => s.RMultiple is not null && s.Horizon == horizon && s.Signals.Count > 0)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        if (items.Count < MinSamples)
        {
            return new WeightLearning(horizon, items.Count, 0, 0, null, null, false, $"Só há {items.Count} sugestões fechadas; são precisas {MinSamples} para propor pesos.", []);
        }

        int split = (int)(items.Count * TrainShare);
        List<SuggestionDto> train = items.Take(split).ToList();
        List<SuggestionDto> test = items.Skip(split).ToList();
        double baseRate = train.Count(s => ModelReliability.R(s) > 0) / (double)train.Count;
        Dictionary<string, double> proposed = new(currentWeights);
        List<LearnedWeight> learned = [];

        foreach (IGrouping<string, (string Name, string Label, bool Win)> group in train
            .SelectMany(s => s.Signals
                .Where(x => Math.Sign(x.Value) == Direction(s) && Math.Abs(x.Value) >= AgreementFloor)
                .Select(x => (x.Name, x.Label, Win: ModelReliability.R(s) > 0)))
            .GroupBy(x => x.Name))
        {
            if (!currentWeights.TryGetValue(group.Key, out double current))
            {
                continue;
            }

            int n = group.Count();
            double lift = group.Count(x => x.Win) / (double)n - baseRate;
            double factor = Math.Clamp(1 + Gain * lift * n / (n + ShrinkSamples), MinFactor, MaxFactor);
            proposed[group.Key] = Math.Round(current * factor, 2);
            learned.Add(new LearnedWeight(group.Key, group.First().Label, current, proposed[group.Key], n, lift));
        }

        double? aucCurrent = Auc(test, currentWeights);
        double? aucProposed = Auc(test, proposed);
        bool validated = test.Count >= MinTestSamples && aucCurrent is not null && aucProposed is not null && aucProposed >= aucCurrent + AucMargin;

        string message = aucCurrent is null || aucProposed is null
            ? "A amostra de teste não tem vencedoras e perdedoras suficientes para validar."
            : validated
                ? $"Validado fora da amostra: a capacidade de separar ganhos de perdas (AUC) sobe de {aucCurrent:0.00} para {aucProposed:0.00} em {test.Count} sugestões que não foram usadas para aprender."
                : $"Não validado: nas {test.Count} sugestões de teste o AUC passa de {aucCurrent:0.00} para {aucProposed:0.00}, sem melhoria suficiente (mínimo +{AucMargin:0.00}). Os pesos atuais mantêm-se.";

        return new WeightLearning(horizon, items.Count, train.Count, test.Count, aucCurrent, aucProposed, validated, message, learned.OrderByDescending(l => Math.Abs(l.Lift)).ToList());
    }

    public static double? Auc(IReadOnlyList<SuggestionDto> items, IReadOnlyDictionary<string, double> weights)
    {
        List<(double Strength, bool Win)> scored = items.Select(s => (Strength: Strength(s, weights), Win: ModelReliability.R(s) > 0)).ToList();
        List<double> wins = scored.Where(x => x.Win).Select(x => x.Strength).ToList();
        List<double> losses = scored.Where(x => !x.Win).Select(x => x.Strength).ToList();

        if (wins.Count == 0 || losses.Count == 0)
        {
            return null;
        }

        double favourable = 0;

        foreach (double win in wins)
        {
            foreach (double loss in losses)
            {
                favourable += win > loss ? 1 : win == loss ? 0.5 : 0;
            }
        }

        return favourable / ((double)wins.Count * losses.Count);
    }

    private static double Strength(SuggestionDto suggestion, IReadOnlyDictionary<string, double> weights)
    {
        double weightSum = 0;
        double total = 0;

        foreach (SignalResult signal in suggestion.Signals)
        {
            double weight = weights.GetValueOrDefault(signal.Name, signal.Weight);
            weightSum += weight;
            total += weight * signal.Value;
        }

        return weightSum > 0 ? Direction(suggestion) * total / weightSum : 0;
    }

    private static int Direction(SuggestionDto suggestion) => suggestion.Direction == TradeDirection.Buy ? 1 : -1;
}
