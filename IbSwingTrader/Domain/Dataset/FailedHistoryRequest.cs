namespace IbSwingTrader.Domain.Dataset
{
    public class FailedHistoryRequest
    {
        public string Ticker { get; set; } = "";
        public string Problem { get; set; } = "";
    }
}
