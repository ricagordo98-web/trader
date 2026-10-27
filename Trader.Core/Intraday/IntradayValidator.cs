namespace Trader.Core.Intraday;

public static class IntradayValidator
{
    public static IReadOnlyList<string> Validate(IntradayOptions o)
    {
        List<string> errors = [];

        if (o.SlAtrMultiple is < 0.5 or > 10 || o.TpAtrMultiple is < 0.5 or > 20 || o.MinStopAtrMultiple is < 0 or > 10)
        {
            errors.Add("Os múltiplos de ATR para stop (0,5–10), alvo (0,5–20) e stop mínimo (0–10) estão fora do intervalo.");
        }

        if (o.MinRiskReward is < 1 or > 6)
        {
            errors.Add("O R:R mínimo tem de estar entre 1 e 6.");
        }

        if (o.ConfidenceThreshold is < 0.5 or > 0.95 || o.MaxConfidence is < 0.5 or > 0.99)
        {
            errors.Add("A certeza mínima (50–95%) e a máxima (50–99%) estão fora do intervalo.");
        }

        if (o.AvoidFirstMinutes is < 0 or > 120 || o.AvoidLastMinutes is < 0 or > 120)
        {
            errors.Add("As janelas a evitar no início e no fim da sessão têm de estar entre 0 e 120 minutos.");
        }

        if (o.MaxRecentMoveAtr is < 0 or > 20 || o.MaxDayMovePercent is < 0 or > 50)
        {
            errors.Add("Os limites de movimento esticado estão fora do intervalo (0 desliga).");
        }

        if (o.MarketWeight is < 0 or > 5 || o.MarketAlignmentFloorPercent is < 0 or > 5 || o.AvoidMacroHours is < 0 or > 12)
        {
            errors.Add("O peso do mercado (0–5), o limiar de alinhamento (0–5%) e a janela macro (0–12 h) estão fora do intervalo.");
        }

        if (o.MinAdx is < 0 or > 60 || o.WeakCellMinSamples is < 5 or > 200)
        {
            errors.Add("O ADX mínimo (0–60) e o mínimo de casos para desligar uma célula fraca (5–200) estão fora do intervalo.");
        }

        if (o.MaxVix is < 0 or > 100 || o.MaxVolatilityRatio is < 0 or > 20)
        {
            errors.Add("O VIX máximo (0–100) e a volatilidade anormal máxima (0–20) estão fora do intervalo. 0 desliga.");
        }

        if (o.PartialAtR is < 0.1 or > 10 || o.PartialFraction is < 0 or > 0.9 || o.BreakevenAtR is < 0 or > 10 || o.LockR is < -1 or > 5 || o.TrailAfterR is < 0 or > 10 || o.TrailDistanceR is < 0.1 or > 10)
        {
            errors.Add("Os parâmetros da saída inteligente estão fora do intervalo.");
        }

        if (o.ShadowMinConfidence is < 0.3 or > 0.9 || o.MaxShadowPerCycle is < 0 or > 100)
        {
            errors.Add("A certeza mínima das sugestões em sombra (30–90%) e o máximo por ciclo (0–100) estão fora do intervalo.");
        }

        if (o.MaxOpenSuggestions is < 1 or > 30 || o.MaxNewSuggestionsPerCycle is < 1 or > 20)
        {
            errors.Add("O máximo de sugestões abertas (1–30) e novas por ciclo (1–20) estão fora do intervalo.");
        }

        return errors;
    }
}
