
namespace IbSwingTrader.Abstractions.Dataset
{
    public interface IFailedHistoryRequestTableFormatter
    {
        string Format(IEnumerable<FailedHistoryRequest> items);
    }
}
