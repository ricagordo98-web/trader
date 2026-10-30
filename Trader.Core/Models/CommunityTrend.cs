namespace Trader.Core.Models;

public sealed record CommunityTrend(
    int Rank,
    string Symbol,
    string Name,
    int Mentions,
    int? MentionsPrevious,
    int Upvotes,
    int? RankPrevious,
    bool InWatchlist,
    int PostCount,
    double? AverageSentiment);
