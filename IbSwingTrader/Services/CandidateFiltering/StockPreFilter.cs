using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class StockPreFilter : IStockPreFilter
    {
        public decimal MinDollarVolume { get; set; } = 10_000_000m;

        public decimal MinMarketCap { get; set; } = 300_000_000m;

        public bool Pass(StockInfo stock)
        {
            var dollarVolume = stock.Price * stock.AvgVolume20;

            if (dollarVolume < MinDollarVolume)
                return false;

            if (stock.MarketCap < MinMarketCap)
                return false;

            return true;
        }
    }
}
