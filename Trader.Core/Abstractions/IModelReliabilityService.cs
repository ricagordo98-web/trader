using Trader.Core.Models;
using Trader.Core.Scoring;

namespace Trader.Core.Abstractions;

public interface IModelReliabilityService
{
    Task<ModelReliabilityReport> GetReportAsync(SuggestionHorizon? horizon, CancellationToken cancellationToken);

    Task<SentimentStudyResult> GetSentimentStudyAsync(CancellationToken cancellationToken);

    Task<WeightLearning> GetWeightLearningAsync(SuggestionHorizon horizon, CancellationToken cancellationToken);

    Task<WeightLearning> ApplyLearnedWeightsAsync(SuggestionHorizon horizon, CancellationToken cancellationToken);

    Task<MetaModelFit> GetMetaModelAsync(SuggestionHorizon horizon, CancellationToken cancellationToken);

    Task<MetaModelFit> ApplyMetaModelAsync(SuggestionHorizon horizon, CancellationToken cancellationToken);

    Task DisableMetaModelAsync(SuggestionHorizon horizon, CancellationToken cancellationToken);
}
