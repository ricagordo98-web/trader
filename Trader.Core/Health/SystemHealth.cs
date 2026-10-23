using Trader.Core.Models;

namespace Trader.Core.Health;

public enum HealthLevel
{
    Ok,
    Warning,
    Down,
    Off
}

public sealed record HealthItem(string Name, HealthLevel Level, string Detail);

public static class DataFreshness
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(20);

    public static string Describe(DateTimeOffset? asOf, DateTimeOffset now)
    {
        if (asOf is null)
        {
            return "sem hora da cotação";
        }

        TimeSpan age = now - asOf.Value;

        return age.TotalSeconds switch
        {
            < 0 => "agora",
            < 90 => "há segundos",
            < 3600 => $"há {Math.Max(1, (int)age.TotalMinutes)} min",
            < 86_400 => $"há {(int)age.TotalHours} h",
            _ => $"há {(int)age.TotalDays} d"
        };
    }

    public static bool IsStale(DateTimeOffset? asOf, DateTimeOffset now) => asOf is null || now - asOf.Value > StaleAfter;
}

public static class HealthEvaluator
{
    public static HealthItem Prices(IReadOnlyList<InstrumentQuote> quotes, DateTimeOffset now)
    {
        List<InstrumentQuote> priced = quotes.Where(q => q.Price is not null).ToList();

        if (priced.Count == 0)
        {
            return new HealthItem("Preços (Yahoo Finance)", HealthLevel.Down, "Nenhuma cotação disponível. Verifica a ligação à internet.");
        }

        DateTimeOffset? newest = priced.Where(q => q.AsOf is not null).Select(q => q.AsOf).DefaultIfEmpty(null).Max();
        int stale = priced.Count(q => DataFreshness.IsStale(q.AsOf, now));
        string detail = $"{priced.Count} de {quotes.Count} ativos com preço · cotação mais recente {DataFreshness.Describe(newest, now)}.";

        return DataFreshness.IsStale(newest, now)
            ? new HealthItem("Preços (Yahoo Finance)", HealthLevel.Warning, detail + " Pode ser mercado fechado.")
            : new HealthItem("Preços (Yahoo Finance)", HealthLevel.Ok, stale > 0 ? detail + $" {stale} ativos com cotação antiga (mercados fechados)." : detail);
    }

    public static HealthItem News(IReadOnlyList<SourceStatus> sources)
    {
        List<SourceStatus> enabled = sources.Where(s => s.Enabled).ToList();
        List<SourceStatus> failing = enabled.Where(s => s.LastError is not null).ToList();

        if (enabled.Count == 0)
        {
            return new HealthItem("Notícias e sentimento", HealthLevel.Off, "Nenhuma fonte ativa.");
        }

        return failing.Count == 0
            ? new HealthItem("Notícias e sentimento", HealthLevel.Ok, $"{enabled.Count} fontes ativas, sem erros na última ronda.")
            : new HealthItem("Notícias e sentimento", HealthLevel.Warning, $"{failing.Count} de {enabled.Count} fontes com erro: {string.Join(", ", failing.Take(4).Select(s => s.Name))}.");
    }

    public static HealthItem Optional(string name, bool configured, bool? reachable, string onDetail, string offDetail)
    {
        if (!configured)
        {
            return new HealthItem(name, HealthLevel.Off, offDetail);
        }

        return reachable == false
            ? new HealthItem(name, HealthLevel.Down, "Configurado mas sem resposta.")
            : new HealthItem(name, HealthLevel.Ok, onDetail);
    }
}
