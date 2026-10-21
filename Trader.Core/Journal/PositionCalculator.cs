using Trader.Core.Models;

namespace Trader.Core.Journal;

public sealed record PositionInput(
    double Capital,
    double RiskPercent,
    double Entry,
    double StopLoss,
    double? TakeProfit,
    double Leverage,
    double FxRate,
    AssetClass AssetClass);

public sealed record PositionResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    TradeDirection Direction,
    double RiskAmount,
    double StopDistance,
    double StopDistancePercent,
    double Units,
    double WholeUnits,
    double ActualRiskAmount,
    double? Lots,
    double Notional,
    double MarginRequired,
    double? RewardAmount,
    double? RiskReward);

public static class PositionCalculator
{
    public const double ForexLotSize = 100_000;
    public const double MaxRecommendedRiskPercent = 2;
    public const double MinRecommendedRiskReward = 1.5;

    public static double DefaultLeverage(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Forex => 30,
        AssetClass.Commodity => 10,
        AssetClass.Index => 20,
        AssetClass.Crypto => 2,
        _ => 5
    };

    public static PositionResult Calculate(PositionInput input)
    {
        List<string> errors = Validate(input);

        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        TradeDirection direction = input.StopLoss < input.Entry ? TradeDirection.Buy : TradeDirection.Sell;
        double riskAmount = input.Capital * input.RiskPercent / 100;
        double stopDistance = Math.Abs(input.Entry - input.StopLoss);
        double riskPerUnit = stopDistance * input.FxRate;
        double units = riskAmount / riskPerUnit;
        double wholeUnits = Math.Floor(units);
        double notional = units * input.Entry * input.FxRate;
        double margin = notional / input.Leverage;

        double? rewardAmount = null;
        double? riskReward = null;
        List<string> warnings = [];

        if (input.TakeProfit is { } takeProfit)
        {
            bool onProfitSide = direction == TradeDirection.Buy ? takeProfit > input.Entry : takeProfit < input.Entry;

            if (onProfitSide)
            {
                rewardAmount = Math.Abs(takeProfit - input.Entry) * input.FxRate * units;
                riskReward = rewardAmount / riskAmount;
            }
            else
            {
                warnings.Add("O take profit está do lado errado da entrada para esta direção.");
            }
        }

        if (input.RiskPercent > MaxRecommendedRiskPercent)
        {
            warnings.Add($"Arriscar mais de {MaxRecommendedRiskPercent:0}% do capital por operação aumenta muito o risco de ruína numa sequência de perdas.");
        }

        if (riskReward is { } rr && rr < MinRecommendedRiskReward)
        {
            warnings.Add($"R:R de 1:{rr:0.0} é baixo; precisas de acertar muito mais de metade das vezes para compensar custos.");
        }

        if (margin > input.Capital)
        {
            warnings.Add("A margem necessária excede o teu capital: não consegues abrir esta posição com este stop tão curto.");
        }

        if (wholeUnits < 1 && input.AssetClass is AssetClass.Stock or AssetClass.Etf)
        {
            warnings.Add("O risco escolhido não chega para uma unidade inteira. Se a corretora só aceitar ações inteiras, aumenta o risco ou alarga o stop.");
        }

        return new PositionResult(
            true,
            [],
            warnings,
            direction,
            riskAmount,
            stopDistance,
            stopDistance / input.Entry * 100,
            units,
            wholeUnits,
            wholeUnits * riskPerUnit,
            input.AssetClass == AssetClass.Forex ? units / ForexLotSize : null,
            notional,
            margin,
            rewardAmount,
            riskReward);
    }

    private static List<string> Validate(PositionInput input)
    {
        List<string> errors = [];

        if (input.Capital <= 0)
        {
            errors.Add("Indica o capital da conta.");
        }

        if (input.RiskPercent is <= 0 or > 10)
        {
            errors.Add("O risco por operação tem de estar entre 0% e 10%.");
        }

        if (input.Entry <= 0 || input.StopLoss <= 0)
        {
            errors.Add("Indica a entrada e o stop loss.");
        }
        else if (input.Entry == input.StopLoss)
        {
            errors.Add("O stop loss não pode ser igual à entrada.");
        }

        if (input.Leverage < 1)
        {
            errors.Add("A alavancagem mínima é 1.");
        }

        if (input.FxRate <= 0)
        {
            errors.Add("A taxa de conversão tem de ser positiva.");
        }

        return errors;
    }

    private static PositionResult Invalid(IReadOnlyList<string> errors) =>
        new(false, errors, [], TradeDirection.Buy, 0, 0, 0, 0, 0, 0, null, 0, 0, null, null);
}
