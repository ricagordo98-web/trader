namespace Trader.Core.Abstractions;

public interface ITickerExtractor
{
    IReadOnlyList<string> Extract(string text);
}
