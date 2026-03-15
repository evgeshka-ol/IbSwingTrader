using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class StockPreFilter : IStockPreFilter
    {
        public decimal MinPrice { get; set; } = 5m;
        public decimal MaxPrice { get; set; } = 200m;

        public decimal MinMarketCap { get; set; } = 1_000_000_000m;

        public long MinAvgVolume { get; set; } = 1_000_000;

        public decimal MinDollarVolume { get; set; } = 10_000_000m;

        public bool Pass(StockInfo stock)
        {
            if (stock.Price < MinPrice || stock.Price > MaxPrice)
                return false;

            if (stock.MarketCap < MinMarketCap)
                return false;

            if (stock.AvgVolume20 < MinAvgVolume)
                return false;

            var dollarVolume = stock.Price * stock.AvgVolume20;

            if (dollarVolume < MinDollarVolume)
                return false;

            return true;
        }
    }
}
