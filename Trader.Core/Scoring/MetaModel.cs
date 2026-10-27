using Trader.Core.Models;

namespace Trader.Core.Scoring;

public sealed record MetaModel(SuggestionHorizon Horizon, IReadOnlyList<string> Features, double Intercept, IReadOnlyList<double> Coefficients, int Samples)
{
    public double Predict(IReadOnlyList<SignalResult> signals, TradeDirection direction)
    {
        double z = Intercept;
        int sign = direction == TradeDirection.Buy ? 1 : -1;

        for (int i = 0; i < Features.Count; i++)
        {
            SignalResult? signal = signals.FirstOrDefault(s => s.Name == Features[i]);
            z += Coefficients[i] * (signal is null ? 0 : sign * signal.Value);
        }

        return 1 / (1 + Math.Exp(-z));
    }
}

public sealed record MetaModelFit(
    SuggestionHorizon Horizon,
    int Samples,
    int TestSamples,
    double? TestAucModel,
    double? TestAucCurrent,
    bool Validated,
    string Message,
    IReadOnlyList<MetaFeatureWeight> Features,
    MetaModel? Model);

public sealed record MetaFeatureWeight(string Signal, double Coefficient);

public static class MetaModelTrainer
{
    public const int MinSamples = 200;
    public const int MinTestSamples = 40;
    public const double AucMargin = 0.03;

    private const double TrainShare = 0.7;
    private const double Regularization = 0.05;
    private const double LearningRate = 0.2;
    private const int Iterations = 600;

    public static MetaModelFit Train(SuggestionHorizon horizon, IReadOnlyList<SuggestionDto> closed)
    {
        List<SuggestionDto> items = closed
            .Where(s => s.RMultiple is not null && s.Horizon == horizon && s.Signals.Count > 0)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        if (items.Count < MinSamples)
        {
            return new MetaModelFit(horizon, items.Count, 0, null, null, false, $"Só há {items.Count} sugestões fechadas; são precisas {MinSamples} para treinar o modelo estatístico.", [], null);
        }

        List<string> features = items.SelectMany(s => s.Signals.Select(x => x.Name)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        int split = (int)(items.Count * TrainShare);
        List<SuggestionDto> train = items.Take(split).ToList();
        List<SuggestionDto> test = items.Skip(split).ToList();

        (double intercept, double[] weights) = Fit(train, features);
        MetaModel model = new(horizon, features, intercept, weights, items.Count);

        double? aucModel = Auc(test, s => model.Predict(s.Signals, s.Direction));
        double? aucCurrent = Auc(test, s => s.Confidence);
        bool validated = test.Count >= MinTestSamples && aucModel is not null && aucCurrent is not null && aucModel >= aucCurrent + AucMargin;

        string message = aucModel is null || aucCurrent is null
            ? "A amostra de teste não tem vencedoras e perdedoras suficientes para validar."
            : validated
                ? $"Validado fora da amostra: o AUC sobe de {aucCurrent:0.00} (certeza atual) para {aucModel:0.00} (modelo) em {test.Count} sugestões que não foram usadas no treino."
                : $"Não validado: nas {test.Count} sugestões de teste o AUC passa de {aucCurrent:0.00} para {aucModel:0.00}, sem melhoria suficiente (mínimo +{AucMargin:0.00}). A certeza atual mantém-se.";

        // The final model is refitted on every sample only after validation; the held-out check above is what justifies it.
        MetaModel final = validated ? Refit(horizon, items, features) : model;

        return new MetaModelFit(
            horizon,
            items.Count,
            test.Count,
            aucModel,
            aucCurrent,
            validated,
            message,
            features.Select((f, i) => new MetaFeatureWeight(f, final.Coefficients[i])).OrderByDescending(f => Math.Abs(f.Coefficient)).ToList(),
            validated ? final : null);
    }

    private static MetaModel Refit(SuggestionHorizon horizon, List<SuggestionDto> items, List<string> features)
    {
        (double intercept, double[] weights) = Fit(items, features);
        return new MetaModel(horizon, features, intercept, weights, items.Count);
    }

    private static (double Intercept, double[] Weights) Fit(IReadOnlyList<SuggestionDto> items, IReadOnlyList<string> features)
    {
        double[][] x = items.Select(s => Vector(s, features)).ToArray();
        double[] y = items.Select(s => ModelReliability.R(s) > 0 ? 1.0 : 0.0).ToArray();
        double[] w = new double[features.Count];
        double b = Math.Log(Math.Max(0.01, y.Average()) / Math.Max(0.01, 1 - y.Average()));

        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            double[] gradient = new double[w.Length];
            double gradientB = 0;

            for (int i = 0; i < x.Length; i++)
            {
                double z = b;

                for (int j = 0; j < w.Length; j++)
                {
                    z += w[j] * x[i][j];
                }

                double error = 1 / (1 + Math.Exp(-z)) - y[i];
                gradientB += error;

                for (int j = 0; j < w.Length; j++)
                {
                    gradient[j] += error * x[i][j];
                }
            }

            b -= LearningRate * gradientB / x.Length;

            for (int j = 0; j < w.Length; j++)
            {
                w[j] -= LearningRate * (gradient[j] / x.Length + Regularization * w[j]);
            }
        }

        return (b, w);
    }

    private static double[] Vector(SuggestionDto s, IReadOnlyList<string> features)
    {
        int sign = s.Direction == TradeDirection.Buy ? 1 : -1;
        return features.Select(f => s.Signals.FirstOrDefault(x => x.Name == f) is { } signal ? sign * signal.Value : 0).ToArray();
    }

    private static double? Auc(IReadOnlyList<SuggestionDto> items, Func<SuggestionDto, double> score)
    {
        List<double> wins = items.Where(s => ModelReliability.R(s) > 0).Select(score).ToList();
        List<double> losses = items.Where(s => ModelReliability.R(s) <= 0).Select(score).ToList();

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
}
