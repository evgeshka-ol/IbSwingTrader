using System.Text.Json;

namespace IbSwingTrader.Application.Candidates
{
    public class StockPreFilter(
        ITextLogger logger,
        IGetCandidatesSettingsProvider settingsProvider,
        IAgentPathService pathService) : IStockPreFilter
    {
        private readonly ITextLogger _logger = logger;
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;
        private readonly IAgentPathService _pathService = pathService;
        private IReadOnlySet<string>? _tickerSuffixExceptions;

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
                    if (GetTickerSuffixExceptions(settings).Contains(stock.Ticker))
                    {
                        _logger.Info(
                            $"Stock {stock.Ticker} ends with '{suffix}', but is allowed by ticker suffix exception list.");
                        continue;
                    }

                    _logger.Info(
                        $"Stock {stock.Ticker} ends with '{suffix}', which is not supported.");
                    return false;
                }
            }

            return true;
        }

        private IReadOnlySet<string> GetTickerSuffixExceptions(PreFilterSettings settings)
        {
            if (_tickerSuffixExceptions != null)
                return _tickerSuffixExceptions;

            var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(settings.TickerSuffixExceptionFile))
                return _tickerSuffixExceptions = tickers;

            try
            {
                var path = Path.IsPathRooted(settings.TickerSuffixExceptionFile)
                    ? settings.TickerSuffixExceptionFile
                    : Path.Combine(_pathService.GetDataRoot(), settings.TickerSuffixExceptionFile);

                if (!File.Exists(path))
                {
                    _logger.Info($"Ticker suffix exception file not found: {path}");
                    return _tickerSuffixExceptions = tickers;
                }

                var json = File.ReadAllText(path);
                var exceptions = JsonSerializer.Deserialize<TickerSuffixExceptionList>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (exceptions?.Tickers == null)
                    return _tickerSuffixExceptions = tickers;

                foreach (var ticker in exceptions.Tickers)
                {
                    if (!string.IsNullOrWhiteSpace(ticker))
                        tickers.Add(ticker.Trim());
                }

                _logger.Info($"Ticker suffix exceptions loaded: Count={tickers.Count}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load ticker suffix exception file: {ex.Message}");
            }

            return _tickerSuffixExceptions = tickers;
        }
    }
}
