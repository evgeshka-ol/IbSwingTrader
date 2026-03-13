using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ITwsConnection
    {
        EClientSocket Client { get; }
        bool IsConnected { get; }
        TaskCompletionSource<bool> Ready { get; }

        void Connect(string host = "127.0.0.1", int port = 7496, int clientId = 1);
        Task<List<ContractDetails>> GetContractDetails(Contract contract);
        Task<List<Candle>> RequestHistoricalData(Contract contract, Timeframe timeframe, DateTime endTimeUtc, int bars);
    }
}