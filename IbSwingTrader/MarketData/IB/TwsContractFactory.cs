using IBApi;

namespace IbSwingTrader.MarketData.IB
{
    public static class TwsContractFactory
    {
        public static Contract CreateStock(string ticker)
        {
            return new Contract
            {
                Symbol = ticker,
                SecType = "STK",
                Exchange = "SMART",
                Currency = "USD"
            };
        }

        public static Contract CreateIndex(string ticker)
        {
            return new Contract
            {
                Symbol = ticker,
                SecType = "IND",
                Exchange = "CBOE",
                Currency = "USD"
            };
        }
    }
}
