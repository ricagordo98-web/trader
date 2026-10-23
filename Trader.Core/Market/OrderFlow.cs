namespace Trader.Core.Market;

public sealed record FlowBucket(DateTimeOffset Time, double Buy, double Sell, int Trades)
{
    public double Delta => Buy - Sell;
}

public sealed record OrderFlowSnapshot(
    string Symbol,
    bool Available,
    string Note,
    DateTimeOffset? UpdatedAt,
    double? Imbalance,
    double BuyShare,
    double CumulativeDelta,
    double TotalVolume,
    IReadOnlyList<FlowBucket> Buckets);

public sealed class FlowAggregator
{
    public const int MaxBuckets = 120;
    private static readonly TimeSpan BucketLength = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly SortedDictionary<DateTimeOffset, (double Buy, double Sell, int Trades)> _buckets = [];
    private double? _imbalance;
    private DateTimeOffset? _updatedAt;

    public void AddTrade(DateTimeOffset time, double quantity, bool buyerIsMaker)
    {
        if (quantity <= 0)
        {
            return;
        }

        DateTimeOffset start = Floor(time);

        lock (_gate)
        {
            _buckets.TryGetValue(start, out (double Buy, double Sell, int Trades) current);
            _buckets[start] = buyerIsMaker
                ? (current.Buy, current.Sell + quantity, current.Trades + 1)
                : (current.Buy + quantity, current.Sell, current.Trades + 1);

            while (_buckets.Count > MaxBuckets)
            {
                _buckets.Remove(_buckets.Keys.First());
            }

            _updatedAt = time;
        }
    }

    public void SetDepth(IReadOnlyList<double> bidQuantities, IReadOnlyList<double> askQuantities, DateTimeOffset time)
    {
        double bids = bidQuantities.Sum();
        double asks = askQuantities.Sum();

        lock (_gate)
        {
            _imbalance = bids + asks > 0 ? (bids - asks) / (bids + asks) : null;
            _updatedAt = time;
        }
    }

    public OrderFlowSnapshot Snapshot(string symbol, DateTimeOffset now, int minutes = 60)
    {
        lock (_gate)
        {
            DateTimeOffset from = Floor(now) - TimeSpan.FromMinutes(minutes - 1);
            List<FlowBucket> buckets = [];

            for (DateTimeOffset t = from; t <= Floor(now); t += BucketLength)
            {
                _buckets.TryGetValue(t, out (double Buy, double Sell, int Trades) value);
                buckets.Add(new FlowBucket(t, value.Buy, value.Sell, value.Trades));
            }

            double buy = buckets.Sum(b => b.Buy);
            double sell = buckets.Sum(b => b.Sell);
            double total = buy + sell;
            bool hasData = total > 0 || _imbalance is not null;

            return new OrderFlowSnapshot(
                symbol,
                hasData,
                hasData ? "Volume em unidades do ativo, agressor comprador contra agressor vendedor, na Binance." : "A recolher dados da Binance; os primeiros minutos aparecem em instantes.",
                _updatedAt,
                _imbalance,
                total > 0 ? buy / total : 0.5,
                buy - sell,
                total,
                buckets);
        }
    }

    private static DateTimeOffset Floor(DateTimeOffset time) =>
        new(time.UtcDateTime.Ticks - time.UtcDateTime.Ticks % BucketLength.Ticks, TimeSpan.Zero);
}

public static class FlowSymbols
{
    public static string? ToBinanceStream(string symbol)
    {
        string upper = symbol.Trim().ToUpperInvariant();

        if (!upper.EndsWith("-USD", StringComparison.Ordinal) || upper.Length < 6 || upper.Length > 13)
        {
            return null;
        }

        string coin = upper[..^4];
        return coin.All(char.IsLetterOrDigit) ? $"{coin.ToLowerInvariant()}usdt" : null;
    }
}
