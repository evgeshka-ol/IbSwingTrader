using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IFailedHistoryRequestTableFormatter
    {
        string Format(IEnumerable<FailedHistoryRequest> items);
    }
}
