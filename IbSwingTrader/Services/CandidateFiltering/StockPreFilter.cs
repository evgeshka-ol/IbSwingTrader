using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class StockPreFilter : IStockPreFilter
    {
        public bool Pass(StockInfo stock)
        {
            //if (stock == null)
            //    return false;

            //if (string.IsNullOrWhiteSpace(stock.Ticker))
            //    return false;

            //if (!string.Equals(stock.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            //    return false;

            //if (!string.IsNullOrWhiteSpace(stock.StockType) &&
            //    !stock.StockType.Contains("CORP", StringComparison.OrdinalIgnoreCase) &&
            //    !stock.StockType.Contains("ADR", StringComparison.OrdinalIgnoreCase))
            //    return false;

            //if (stock.Ticker.EndsWith("U", StringComparison.OrdinalIgnoreCase))
            //    return false;

            //if (stock.Ticker.EndsWith("W", StringComparison.OrdinalIgnoreCase))
            //    return false;

            return true;
        }
    }
}
