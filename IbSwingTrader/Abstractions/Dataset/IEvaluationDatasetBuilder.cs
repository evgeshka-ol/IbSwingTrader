namespace IbSwingTrader.Abstractions.Dataset
{
    public interface IEvaluationDatasetBuilder
    {
        Task RunAsync();

        Task RefreshFeedbackAsync();

        Task<List<EvaluationDatasetRow>> ReadCurrentAsync();

        Task UpsertAsync(List<CandidateEvaluationResult> evaluations);
    }
}
