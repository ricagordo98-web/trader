using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Core.Abstractions;

public interface IModelReliabilityService
{
    Task<ModelReliabilityReport> GetReportAsync(SuggestionHorizon? horizon, CancellationToken cancellationToken);

    Task<SentimentStudyResult> GetSentimentStudyAsync(CancellationToken cancellationToken);
}
