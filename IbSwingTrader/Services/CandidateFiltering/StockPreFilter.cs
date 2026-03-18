using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class StockPreFilter(ITextLogger logger) : IStockPreFilter
    {
        private readonly ITextLogger _logger = logger;
        private static readonly HashSet<string> DenyList = new(StringComparer.OrdinalIgnoreCase)
            {
                "UVIX",
                "AGQ",
                "SCO",
                "MSTZ"
            };

        public bool Pass(StockInfo stock)
        {
            if (stock == null)
            {
                _logger.Info("Stock is null.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(stock.Ticker))
            {
                _logger.Info("Stock ticker is null or whitespace.");
                return false;
            }

            if (DenyList.Contains(stock.Ticker))
            {
                _logger.Info($"Stock {stock.Ticker} is in deny-list.");
                return false;
            }

            if (!string.Equals(stock.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info($"Stock {stock.Ticker} has unsupported currency: {stock.Currency}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(stock.StockType))
            {
                var type = stock.StockType.Trim();

                var isSupported =
                    type.Contains("STK", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("CORP", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("ADR", StringComparison.OrdinalIgnoreCase);

                if (!isSupported)
                {
                    _logger.Info($"Stock {stock.Ticker} has unsupported stock type: {stock.StockType}");
                    return false;
                }
            }

            if (stock.Ticker.EndsWith("U", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info($"Stock {stock.Ticker} is a unit (ends with 'U'), which is not supported.");
                return false;
            }

            if (stock.Ticker.EndsWith("W", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info($"Stock {stock.Ticker} is a warrant (ends with 'W'), which is not supported.");
                return false;
            }

            return true;
        }
    }
}
