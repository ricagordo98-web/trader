using Trader.Core.Abstractions;

namespace Trader.Infrastructure.News;

public sealed class NullSentimentAnalyzer : ISentimentAnalyzer
{
    public double? Analyze(string text) => null;
}
