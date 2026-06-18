using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Domain.Dataset;

namespace IbSwingTrader.Abstractions.Evaluation
{
    public interface ICandidatePatternVerdictService
    {
        CandidatePatternVerdict Analyze(CandidateEvaluationResult candidate);

        CandidatePatternVerdict Analyze(EvaluationDatasetRow row);
    }

    public sealed record CandidatePatternVerdict(
        string DetectedPipeline,
        string DetectedPattern,
        string PatternVerdict,
        string PatternVerdictReason);
}
