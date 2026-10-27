namespace Trader.Core.Scoring;

public static class ScoringValidator
{
    public static IReadOnlyList<string> Validate(ScoringOptions o)
    {
        List<string> errors = [];

        foreach ((string name, double weight) in new[]
        {
            ("Sentimento", o.SentimentWeight), ("Volume de menções", o.BuzzWeight), ("Tendência", o.TrendWeight),
            ("Momentum", o.MomentumWeight), ("Bollinger", o.BollingerWeight), ("Força relativa", o.RelativeStrengthWeight)
        })
        {
            if (weight is < 0 or > 5)
            {
                errors.Add($"O peso de {name} tem de estar entre 0 e 5.");
            }
        }

        if (o.SentimentWeight + o.BuzzWeight + o.TrendWeight + o.MomentumWeight + o.BollingerWeight <= 0)
        {
            errors.Add("Pelo menos um peso tem de ser superior a 0.");
        }

        if (o.ConfidenceThreshold is < 0.5 or > 0.95)
        {
            errors.Add("A certeza mínima tem de estar entre 50% e 95%.");
        }

        if (o.MinAgreeingSignals is < 2 or > 5)
        {
            errors.Add("O número mínimo de sinais concordantes tem de estar entre 2 e 5.");
        }

        if (o.SlAtrMultiple is < 0.5 or > 5 || o.TpAtrMultiple is < 0.5 or > 10)
        {
            errors.Add("Os múltiplos de ATR para SL (0,5–5) e TP (0,5–10) estão fora do intervalo.");
        }

        if (o.ShadowMinConfidence is < 0.3 or > 0.9)
        {
            errors.Add("A certeza mínima das sugestões em sombra tem de estar entre 30% e 90%.");
        }

        if (o.MinRiskReward is < 1 or > 5)
        {
            errors.Add("O R:R mínimo tem de estar entre 1 e 5.");
        }

        if (o.MinAdx is < 0 or > 60 || o.MaxVix is < 0 or > 100)
        {
            errors.Add("O ADX mínimo (0–60) e o VIX máximo (0–100) estão fora do intervalo.");
        }

        if (o.BlockEarningsWithinDays is < 0 or > 30 || o.WeakCellMinSamples is < 5 or > 200)
        {
            errors.Add("Os dias antes de resultados (0–30) e o mínimo de casos para desligar uma célula fraca (5–200) estão fora do intervalo.");
        }

        if (o.ExpiryDays is < 1 or > 30)
        {
            errors.Add("A validade tem de estar entre 1 e 30 dias.");
        }

        return errors;
    }
}
