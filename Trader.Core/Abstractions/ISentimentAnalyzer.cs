namespace Trader.Core.Abstractions;

public interface ISentimentAnalyzer
{
    double? Analyze(string text);
}
