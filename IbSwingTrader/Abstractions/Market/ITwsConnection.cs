using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface ITwsConnection
    {
        EClientSocket Client { get; }
        bool IsConnected { get; }
        TaskCompletionSource<bool> Ready { get; }

        void Connect(string host = "127.0.0.1", int port = 7496, int clientId = 1);
        Task<List<ContractDetails>> GetContractDetails(Contract contract, TimeSpan? timeout = null);
        Task<List<Candle>> RequestHistoricalData(Contract contract, Timeframe timeframe, DateTime endTimeUtc, int bars);
        Task<List<StockInfo>> GetStocksAsync(ScannerSubscription subscription, List<TagValue> filters);
        Task<string> RequestScannerParametersAsync();
        Task<FundamentalSnapshot?> GetFundamentalSnapshotAsync(Contract contract);
        Task<List<string>> ProbeMarketDataAsync(Contract contract, int seconds = 10);
    }
}
