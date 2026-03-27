
namespace IbSwingTrader.Application.Candidates
{
    public class StockPreFilter(
        ITextLogger logger,
        IGetCandidatesSettingsProvider settingsProvider) : IStockPreFilter
    {
        private readonly ITextLogger _logger = logger;
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;

        public bool Pass(StockInfo stock)
        {
            var settings = _settingsProvider.Get().PreFilter;

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

            if (settings.DenyList.Contains(
                    stock.Ticker,
                    StringComparer.OrdinalIgnoreCase))
            {
                _logger.Info($"Stock {stock.Ticker} is in deny-list.");
                return false;
            }

            if (!string.Equals(
                    stock.Currency,
                    settings.RequiredCurrency,
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info(
                    $"Stock {stock.Ticker} has unsupported currency: {stock.Currency}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(stock.StockType))
            {
                var type = stock.StockType.Trim();

                var isSupported = settings.AllowedStockTypeMarkers.Any(
                    marker => type.Contains(marker, StringComparison.OrdinalIgnoreCase));

                if (!isSupported)
                {
                    _logger.Info(
                        $"Stock {stock.Ticker} has unsupported stock type: {stock.StockType}");
                    return false;
                }
            }

            foreach (var suffix in settings.RejectTickersEndingWith)
            {
                if (stock.Ticker.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info(
                        $"Stock {stock.Ticker} ends with '{suffix}', which is not supported.");
                    return false;
                }
            }

            return true;
        }
    }
}