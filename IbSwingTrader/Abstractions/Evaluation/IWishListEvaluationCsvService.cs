namespace IbSwingTrader.Abstractions.Evaluation
{
    public interface IWishListEvaluationCsvService
    {
        Task WriteAsync(string path, List<WishListEvaluationRecord> records);
    }
}
