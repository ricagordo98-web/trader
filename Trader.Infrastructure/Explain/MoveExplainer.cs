using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Abstractions;
using Trader.Core.Explain;
using Trader.Core.Models;
using Trader.Infrastructure.Persistence;

namespace Trader.Infrastructure.Explain;

public sealed class MoveExplainer(
    TraderDbContext dbContext,
    IMarketDataService marketData,
    IEnumerable<IEvidenceSource> sources,
    IMoveNarrator narrator,
    ISentimentAnalyzer sentimentAnalyzer,
    ILogger<MoveExplainer> logger) : IMoveExplainer
{
    private const int MaxPeers = 5;
    private const int MinBetaPoints = 30;
    private const double MinPeerCorrelation = 0.6;
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecentCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PastCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan LongWindow = TimeSpan.FromDays(3);
    private static readonly TimeSpan CachedLineAge = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MoveExplanation?> ExplainAsync(MoveRequest request, CancellationToken cancellationToken)
    {
        Instrument? instrument = await dbContext.Instruments.AsNoTracking().FirstOrDefaultAsync(i => i.Symbol == request.Symbol, cancellationToken);

        if (instrument is null)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        (DateTimeOffset from, DateTimeOffset to)? window = await ResolveWindowAsync(instrument.Symbol, request, cancellationToken);

        if (window is null)
        {
            return null;
        }

        (DateTimeOffset from, DateTimeOffset to) = window.Value;
        from = Floor(from);
        to = Floor(to);

        bool wantsNarrative = request.Narrate && narrator.IsEnabled;

        if (!request.Refresh && await FindCachedAsync(instrument.Symbol, from, to, now, wantsNarrative, cancellationToken) is { } cached)
        {
            return cached;
        }

        ChartPlan plan = ChartPlan.For(from, to, now);
        IReadOnlyList<Candle> series = await marketData.GetCandlesAsync(instrument.Symbol, plan.Interval, plan.Range, cancellationToken);
        List<string> notes = [];

        BenchmarkInput? benchmark = await LoadBenchmarkAsync(instrument, plan, notes, cancellationToken);
        IReadOnlyList<PeerMove> peers = await LoadPeersAsync(instrument, plan, from, to, cancellationToken);
        MoveAnalysis? analysis = MoveAnalyzer.Analyze(instrument.Symbol, instrument.Name, plan.Interval, series, from, to, benchmark, peers);

        if (analysis is null)
        {
            return null;
        }

        IReadOnlyList<EvidenceItem> evidence = Score(await GatherEvidenceAsync(instrument, analysis, notes, cancellationToken));
        IReadOnlyList<MoveCause> causes = CauseRanker.Rank(analysis, evidence);
        string summary = ExplanationText.Summary(analysis, causes);

        NarrationResult narration = analysis.Scope == MoveScope.Small || !request.Narrate
            ? new NarrationResult(null, NarrativeStatus.Disabled, null, [])
            : await narrator.NarrateAsync(analysis, causes, cancellationToken);

        if (narration.Status == NarrativeStatus.Rejected)
        {
            notes.Add("O texto do modelo local foi descartado por conter factos que não estavam nas fontes; mostro o resumo automático.");
        }
        else if (narration.Status == NarrativeStatus.Unavailable)
        {
            notes.Add("O modelo local não respondeu; mostro o resumo automático.");
        }

        MoveExplanation explanation = new(analysis, causes, summary, narration.Text, narration.Status, narration.Source, notes, now);
        await SaveAsync(instrument.Symbol, from, to, explanation, narration, now, cancellationToken);
        return explanation;
    }

    public async Task<ExplainStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, int> counts = await dbContext.MoveExplanations
            .AsNoTracking()
            .GroupBy(r => r.NarrativeStatus)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        int Count(string status) => counts.GetValueOrDefault(status);

        return new ExplainStats(
            counts.Values.Sum(),
            Count(NarrativeStatus.Accepted),
            Count(NarrativeStatus.Rejected),
            Count(NarrativeStatus.Disabled),
            Count(NarrativeStatus.Unavailable));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCachedOneLinersAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        DateTimeOffset oldest = DateTimeOffset.UtcNow - CachedLineAge;
        List<MoveExplanationRecord> records = await dbContext.MoveExplanations
            .AsNoTracking()
            .Where(r => symbols.Contains(r.Symbol) && r.CreatedAt >= oldest)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        Dictionary<string, string> lines = [];

        foreach (MoveExplanationRecord record in records.DistinctBy(r => r.Symbol))
        {
            MoveExplanation? explanation = JsonSerializer.Deserialize<MoveExplanation>(record.Json, JsonOptions);

            if (explanation is not null && ExplanationText.OneLiner(explanation) is { } line)
            {
                lines[record.Symbol] = line;
            }
        }

        return lines;
    }

    private async Task<(DateTimeOffset, DateTimeOffset)?> ResolveWindowAsync(string symbol, MoveRequest request, CancellationToken cancellationToken)
    {
        if (request.From is { } from && request.To is { } to && to > from)
        {
            return (from, to);
        }

        IReadOnlyList<Candle> recent = await marketData.GetCandlesAsync(symbol, "5m", "5d", cancellationToken);

        if (recent.Count == 0)
        {
            return null;
        }

        DateTime lastDay = recent.Max(c => c.Time).UtcDateTime.Date;
        List<Candle> session = recent.Where(c => c.Time.UtcDateTime.Date == lastDay).OrderBy(c => c.Time).ToList();
        return (session[0].Time, session[^1].Time);
    }

    private async Task<BenchmarkInput?> LoadBenchmarkAsync(Instrument instrument, ChartPlan plan, List<string> notes, CancellationToken cancellationToken)
    {
        (string Symbol, string Name)? reference = BenchmarkSelector.For(instrument.Symbol, instrument.AssetClass);

        if (reference is null || reference.Value.Symbol == instrument.Symbol)
        {
            return null;
        }

        try
        {
            IReadOnlyList<Candle> candles = await marketData.GetCandlesAsync(reference.Value.Symbol, plan.Interval, plan.Range, cancellationToken);
            double beta = await BetaAsync(instrument.Symbol, reference.Value.Symbol, cancellationToken);
            return new BenchmarkInput(reference.Value.Symbol, reference.Value.Name, candles, beta);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            notes.Add($"Não foi possível obter o mercado de referência ({reference.Value.Name}).");
            logger.LogDebug("Benchmark unavailable: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<double> BetaAsync(string symbol, string benchmark, CancellationToken cancellationToken)
    {
        IReadOnlyList<Candle> own = await marketData.GetCandlesAsync(symbol, "1d", "1y", cancellationToken);
        IReadOnlyList<Candle> reference = await marketData.GetCandlesAsync(benchmark, "1d", "1y", cancellationToken);

        Dictionary<DateTime, double> referenceReturns = Returns(reference);
        List<(double Own, double Reference)> pairs = Returns(own)
            .Where(r => referenceReturns.ContainsKey(r.Key))
            .Select(r => (r.Value, referenceReturns[r.Key]))
            .ToList();

        if (pairs.Count < MinBetaPoints)
        {
            return 1;
        }

        double meanReference = pairs.Average(p => p.Reference);
        double meanOwn = pairs.Average(p => p.Own);
        double variance = pairs.Sum(p => Math.Pow(p.Reference - meanReference, 2));

        return variance > 0 ? Math.Clamp(pairs.Sum(p => (p.Own - meanOwn) * (p.Reference - meanReference)) / variance, 0, 3) : 1;
    }

    private static Dictionary<DateTime, double> Returns(IReadOnlyList<Candle> candles)
    {
        List<Candle> ordered = candles.Where(c => c.Close > 0).OrderBy(c => c.Time).ToList();
        Dictionary<DateTime, double> returns = [];

        for (int i = 1; i < ordered.Count; i++)
        {
            returns[ordered[i].Time.UtcDateTime.Date] = (ordered[i].Close - ordered[i - 1].Close) / ordered[i - 1].Close;
        }

        return returns;
    }

    private async Task<IReadOnlyList<PeerMove>> LoadPeersAsync(Instrument instrument, ChartPlan plan, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<InstrumentQuote> quotes = await marketData.GetQuotesAsync(instrument.AssetClass, cancellationToken);
            InstrumentQuote? own = quotes.FirstOrDefault(q => q.Symbol == instrument.Symbol);

            if (own is null || own.Sparkline.Count < 8)
            {
                return [];
            }

            List<(InstrumentQuote Quote, double Correlation)> correlated = quotes
                .Where(q => q.Symbol != instrument.Symbol && q.Sparkline.Count >= 8)
                .Select(q => (Quote: q, Correlation: Correlation(own.Sparkline, q.Sparkline)))
                .Where(x => x.Correlation >= MinPeerCorrelation)
                .OrderByDescending(x => x.Correlation)
                .Take(MaxPeers)
                .ToList();

            PeerMove?[] moves = await Task.WhenAll(correlated.Select(async x =>
            {
                try
                {
                    IReadOnlyList<Candle> candles = await marketData.GetCandlesAsync(x.Quote.Symbol, plan.Interval, plan.Range, cancellationToken);
                    return MoveAnalyzer.ChangeOver(candles, from, to) is { } change ? new PeerMove(x.Quote.Symbol, x.Quote.Name, change, x.Correlation) : null;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }));

            return moves.OfType<PeerMove>().ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Peers unavailable: {Message}", ex.Message);
            return [];
        }
    }

    private static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int n = Math.Min(a.Count, b.Count);
        double[] left = Diffs(a.TakeLast(n).ToList());
        double[] right = Diffs(b.TakeLast(n).ToList());
        double meanLeft = left.Average();
        double meanRight = right.Average();
        double covariance = left.Zip(right, (x, y) => (x - meanLeft) * (y - meanRight)).Sum();
        double deviation = Math.Sqrt(left.Sum(x => Math.Pow(x - meanLeft, 2)) * right.Sum(y => Math.Pow(y - meanRight, 2)));

        return deviation > 0 ? covariance / deviation : 0;
    }

    private static double[] Diffs(List<double> values) =>
        Enumerable.Range(1, values.Count - 1).Select(i => values[i - 1] > 0 ? (values[i] - values[i - 1]) / values[i - 1] : 0).ToArray();

    private async Task<IReadOnlyList<EvidenceItem>> GatherEvidenceAsync(Instrument instrument, MoveAnalysis analysis, List<string> notes, CancellationToken cancellationToken)
    {
        bool longWindow = analysis.To - analysis.From > LongWindow;
        DateTimeOffset from = longWindow ? analysis.PeakTime.AddDays(-2) : analysis.From;
        DateTimeOffset to = longWindow ? analysis.PeakTime.AddDays(2) : analysis.To;
        EvidenceQuery query = new(instrument.Symbol, instrument.Name, instrument.AssetClass, from, to);

        if (longWindow)
        {
            notes.Add("Janela longa: procurei causas à volta do dia do maior movimento.");
        }

        List<EvidenceItem>[] results = await Task.WhenAll(sources.Select(async source =>
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SourceTimeout);

            try
            {
                return (await source.FetchAsync(query, timeout.Token)).ToList();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Evidence source {Source} failed: {Message}", source.Name, ex.Message);

                lock (notes)
                {
                    notes.Add($"Fonte indisponível: {source.Name}.");
                }

                return [];
            }
        }));

        return results.SelectMany(r => r).ToList();
    }

    private IReadOnlyList<EvidenceItem> Score(IReadOnlyList<EvidenceItem> evidence) =>
        evidence
            .Select(e => e.Sentiment is null && e.Kind == EvidenceKind.Article ? e with { Sentiment = sentimentAnalyzer.Analyze(e.Title) } : e)
            .ToList();

    private async Task<MoveExplanation?> FindCachedAsync(string symbol, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, bool wantsNarrative, CancellationToken cancellationToken)
    {
        TimeSpan ttl = now - to < TimeSpan.FromHours(2) ? RecentCacheDuration : PastCacheDuration;
        DateTimeOffset oldest = now - ttl;

        MoveExplanationRecord? record = await dbContext.MoveExplanations
            .AsNoTracking()
            .Where(r => r.Symbol == symbol && r.From == from && r.To == to && r.CreatedAt >= oldest && (!wantsNarrative || r.NarrativeStatus != NarrativeStatus.Disabled))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return record is null ? null : JsonSerializer.Deserialize<MoveExplanation>(record.Json, JsonOptions);
    }

    private async Task SaveAsync(string symbol, DateTimeOffset from, DateTimeOffset to, MoveExplanation explanation, NarrationResult narration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        dbContext.MoveExplanations.Add(new MoveExplanationRecord
        {
            Symbol = symbol,
            From = from,
            To = to,
            NarrativeStatus = narration.Status,
            Json = JsonSerializer.Serialize(explanation, JsonOptions),
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset Floor(DateTimeOffset time) =>
        new(time.UtcDateTime.Ticks - time.UtcDateTime.Ticks % TimeSpan.FromMinutes(5).Ticks, TimeSpan.Zero);
}

internal sealed record ChartPlan(string Interval, string Range)
{
    public static ChartPlan For(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        TimeSpan span = to - from;
        double age = (now - from).TotalDays;

        if (span <= TimeSpan.FromDays(1.5) && age <= 25)
        {
            return new ChartPlan("5m", age <= 4 ? "5d" : "1mo");
        }

        if (span <= TimeSpan.FromDays(20) && age <= 700)
        {
            return new ChartPlan("60m", age <= 80 ? "3mo" : age <= 170 ? "6mo" : age <= 350 ? "1y" : "2y");
        }

        return new ChartPlan("1d", age <= 350 ? "1y" : age <= 1800 ? "5y" : "max");
    }
}

internal static class BenchmarkSelector
{
    public static (string Symbol, string Name)? For(string symbol, AssetClass assetClass)
    {
        if (assetClass is AssetClass.Forex or AssetClass.Commodity or AssetClass.Index)
        {
            return null;
        }

        if (assetClass == AssetClass.Crypto)
        {
            return symbol == "BTC-USD" ? null : ("BTC-USD", "Bitcoin");
        }

        string upper = symbol.ToUpperInvariant();

        return upper switch
        {
            _ when upper.EndsWith(".DE") => ("^GDAXI", "DAX"),
            _ when upper.EndsWith(".PA") => ("^FCHI", "CAC 40"),
            _ when upper.EndsWith(".MC") => ("^IBEX", "IBEX 35"),
            _ when upper.EndsWith(".LS") => ("PSI20.LS", "PSI-20"),
            _ when upper.EndsWith(".AS") => ("^STOXX50E", "Euro Stoxx 50"),
            _ => ("^GSPC", "S&P 500")
        };
    }
}
