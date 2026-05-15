namespace IbSwingTrader.Abstractions.Dataset
{
    public interface IEvaluationDatasetCsvService
    {
        Task<List<EvaluationDatasetRow>> ReadAsync(string path);

        Task WriteAsync(string path, List<EvaluationDatasetRow> rows);
    }
}
