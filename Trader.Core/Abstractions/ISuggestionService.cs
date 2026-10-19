using Trader.Core.Models;

namespace Trader.Core.Abstractions;

public interface ISuggestionService
{
    Task<IReadOnlyList<SuggestionDto>> GetAsync(string? symbol, bool? open, int limit, CancellationToken cancellationToken);

    Task<TrackRecordDto> GetTrackRecordAsync(CancellationToken cancellationToken);

    Task<int> RefreshAsync(CancellationToken cancellationToken);
}
