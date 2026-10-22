using System.Text.RegularExpressions;

namespace Trader.Core.Explain;

public static partial class CauseRanker
{
    private const int MaxCauses = 6;
    private const double MinScore = 0.45;
    private const double DuplicateTitleSimilarity = 0.55;
    private const double DuplicateBoost = 0.03;
    private const double MaxDuplicateBoost = 0.10;
    private const double HighScore = 0.8;
    private const double MediumScore = 0.55;

    public static IReadOnlyList<MoveCause> Rank(MoveAnalysis analysis, IReadOnlyList<EvidenceItem> evidence)
    {
        bool daily = StepIsDaily(analysis.Interval);
        int moveSign = Math.Sign(analysis.ChangePercent);

        List<(EvidenceItem Item, double Score)> scored = evidence
            .Select(e => (Item: e, Score: Score(e, analysis, daily, moveSign)))
            .Where(s => s.Score >= MinScore)
            .OrderByDescending(s => s.Score)
            .ToList();

        List<(EvidenceItem Item, double Score, int Duplicates)> clusters = [];

        foreach ((EvidenceItem item, double score) in scored)
        {
            int index = clusters.FindIndex(c => Similar(c.Item.Title, item.Title));

            if (index < 0)
            {
                clusters.Add((item, score, 0));
            }
            else
            {
                (EvidenceItem Item, double Score, int Duplicates) cluster = clusters[index];
                clusters[index] = (cluster.Item, Math.Min(1, cluster.Score + Math.Min(DuplicateBoost, MaxDuplicateBoost - cluster.Duplicates * DuplicateBoost)), cluster.Duplicates + 1);
            }
        }

        return clusters
            .OrderByDescending(c => c.Score)
            .Take(MaxCauses)
            .Select((c, i) => new MoveCause(
                $"E{i + 1}",
                c.Item.Category,
                c.Item.Kind,
                c.Item.Title,
                c.Item.Detail,
                c.Item.Url,
                c.Item.Source,
                c.Item.Time,
                Math.Round(c.Score, 2),
                Confidence(c.Score, analysis.Scope, c.Item.Category),
                (int)(analysis.PeakTime - c.Item.Time).TotalMinutes,
                c.Duplicates,
                c.Item.Sentiment))
            .ToList();
    }

    private static double Score(EvidenceItem item, MoveAnalysis analysis, bool daily, int moveSign)
    {
        if (!item.MentionsSymbol && item.Category != CauseCategory.Macro)
        {
            return 0;
        }

        double time = TimeScore(item, analysis, daily);
        double relevance = item.MentionsSymbol ? 1.0 : 0.6;
        double reliability = Math.Min(1, item.Reliability / 1.3);
        double magnitude = Magnitude(item.Category);
        double direction = item.Sentiment is { } sentiment && Math.Abs(sentiment) >= 0.2 && moveSign != 0
            ? Math.Sign(sentiment) == moveSign ? 0.1 : -0.2
            : 0;
        direction += ReactionHeadline(item.Title, moveSign);

        if (OpinionHeadline().IsMatch(item.Title))
        {
            direction -= 0.2;
        }

        return Math.Clamp(0.35 * time + 0.2 * relevance + 0.15 * reliability + 0.25 * magnitude + direction, 0, 1);
    }

    private static double TimeScore(EvidenceItem item, MoveAnalysis analysis, bool daily)
    {
        if (item.Kind == EvidenceKind.Earnings)
        {
            double hours = (analysis.PeakTime - item.Time).TotalHours;

            if (hours is >= -1 and <= 26)
            {
                return 1.0;
            }
        }

        if (daily)
        {
            return item.Time >= analysis.From.AddHours(-18) && item.Time <= analysis.To.AddHours(2) ? 0.7 : 0.3;
        }

        double lead = (analysis.PeakTime - item.Time).TotalMinutes;

        return lead switch
        {
            < -60 => 0.2,
            < 0 => 0.5,
            <= 120 => 1.0 - 0.25 * lead / 120,
            <= 600 => 0.75,
            <= 1440 => 0.6,
            <= 2880 => 0.3,
            _ => 0.15
        };
    }

    private static double ReactionHeadline(string title, int moveSign)
    {
        if (moveSign == 0)
        {
            return 0;
        }

        bool down = DownWords().IsMatch(title);
        bool up = UpWords().IsMatch(title);

        if (down == up)
        {
            return 0;
        }

        return (down ? -1 : 1) == moveSign ? 0.08 : -0.1;
    }

    [GeneratedRegex(@"\b(drops?|dropped|slides?|slid|falls?|fell|plunges?|plunged|tumbles?|tumbled|sinks?|sank|slumps?|slumped|sell-?off|cuts?|slashes|desce|cai|afunda)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DownWords();

    [GeneratedRegex(@"\b(jumps?|jumped|surges?|surged|soars?|soared|rall(?:y|ies|ied)|climbs?|climbed|spikes?|spiked|gains?|gained|rises?|rose|sobe|dispara)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UpWords();

    private static double Magnitude(CauseCategory category) => category switch
    {
        CauseCategory.Earnings => 1.0,
        CauseCategory.MergerAcquisition => 0.95,
        CauseCategory.Guidance => 0.9,
        CauseCategory.Management => 0.8,
        CauseCategory.Legal => 0.8,
        CauseCategory.Analyst => 0.7,
        CauseCategory.Product => 0.65,
        CauseCategory.Dividend => 0.6,
        CauseCategory.Macro => 0.5,
        CauseCategory.Insider => 0.5,
        CauseCategory.Filing => 0.5,
        _ => 0.4
    };

    private static string Confidence(double score, MoveScope scope, CauseCategory category)
    {
        if (scope == MoveScope.Small)
        {
            return "Baixa";
        }

        if (scope == MoveScope.MarketWide || category == CauseCategory.Other)
        {
            return score >= MediumScore ? "Média" : "Baixa";
        }

        return score >= HighScore ? "Alta" : score >= MediumScore ? "Média" : "Baixa";
    }

    [GeneratedRegex(@"\b(prediction|predicts?|should you|could|might|here'?s why|why you should|worth buying|buy now|best stocks?|top \d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpinionHeadline();

    private static bool StepIsDaily(string interval) => interval is "1d" or "1wk" or "1mo";

    private static bool Similar(string a, string b)
    {
        HashSet<string> left = Tokens(a);
        HashSet<string> right = Tokens(b);

        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        return (double)left.Intersect(right).Count() / left.Union(right).Count() >= DuplicateTitleSimilarity;
    }

    private static HashSet<string> Tokens(string text) =>
        Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 3).ToHashSet();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
