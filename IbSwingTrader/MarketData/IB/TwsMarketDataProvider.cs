using IbSwingTrader.MarketData.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsMarketDataProvider(TwsConnection tws) : IMarketDataProvider
    {
        private readonly TwsConnection _tws = tws;

        public Task<List<Candle>> GetCandles(
            string ticker,
            Timeframe timeframe,
            DateTime endTimeUtc,
            int bars)
        {
            return _tws.RequestHistoricalData(
                ticker,
                timeframe,
                endTimeUtc,
                bars);
        }
    }
}
