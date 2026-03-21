using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Commands
{
    public class DownloadFundamentalSnapshotCommand : ICommand
    {
        private readonly ITwsConnection _connection;
        private readonly IContractResolver _contractResolver;
        private readonly ITextLogger _logger;

        public DownloadFundamentalSnapshotCommand(
            ITwsConnection connection,
            IContractResolver contractResolver,
            ITextLogger logger)
        {
            _connection = connection;
            _contractResolver = contractResolver;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            var tickers = new[]
            {
                "AAPL",
                "MSFT",
                "NVDA",
                "BATL",
                "ASM",
                "NG",
                "HYMC"
            };

            var dir = Path.Combine("market-probe");
            Directory.CreateDirectory(dir);

            var successCount = 0;
            var emptyCount = 0;
            var noSubscriptionCount = 0;
            var errorCount = 0;

            foreach (var ticker in tickers)
            {
                try
                {
                    _logger.Info($"Processing {ticker}...");

                    var contract = await _contractResolver.ResolveStockAsync(ticker);
                    var lines = await _connection.ProbeMarketDataAsync(contract, 10);

                    var fileName = $"{ticker}-market-probe.txt";
                    var path = Path.Combine(dir, fileName);

                    await File.WriteAllLinesAsync(path, lines);

                    _logger.Info($"Market probe saved: {path}");

                    var hasNoSubscription = lines.Any(x =>
                        x.Contains("ERROR code=10089", StringComparison.OrdinalIgnoreCase));

                    var hasRealTicks = lines.Any(x =>
                        x.Contains("tickPrice", StringComparison.OrdinalIgnoreCase) ||
                        x.Contains("tickSize", StringComparison.OrdinalIgnoreCase) ||
                        x.Contains("tickGeneric", StringComparison.OrdinalIgnoreCase) ||
                        x.Contains("tickString", StringComparison.OrdinalIgnoreCase));

                    if (hasNoSubscription)
                    {
                        noSubscriptionCount++;
                        _logger.Info($"No API market-data subscription for {ticker}");
                    }
                    else if (hasRealTicks)
                    {
                        successCount++;
                        _logger.Info($"Received real market-data ticks for {ticker}: {lines.Count}");
                    }
                    else if (lines.Count == 0)
                    {
                        emptyCount++;
                        _logger.Info($"No market probe lines received for {ticker}");
                    }
                    else
                    {
                        emptyCount++;
                        _logger.Info($"Only service lines received for {ticker}: {lines.Count}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.Error($"Error processing {ticker}: {ex.Message}");
                }
            }

            _logger.Info(
                $"Market probe finished. " +
                $"Success={successCount}, " +
                $"NoSubscription={noSubscriptionCount}, " +
                $"Empty={emptyCount}, " +
                $"Errors={errorCount}, " +
                $"Total={tickers.Length}");
        }

        private async Task DownloadFundamentalSnapshotAsync(string ticker)
        {
            var contract = await _contractResolver.ResolveStockAsync(ticker);

            var snapshot = await _connection.GetFundamentalSnapshotAsync(contract);

            if (snapshot == null)
            {
                _logger.Error($"No fundamental snapshot received for {ticker}");
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshot.RawXml))
            {
                _logger.Error($"Fundamental snapshot has empty XML for {ticker}");
                return;
            }

            var dir = Path.Combine("Data", "fundamental");
            Directory.CreateDirectory(dir);

            var fileName = $"{ticker}-ReportSnapshot.xml";
            var path = Path.Combine(dir, fileName);

            await File.WriteAllTextAsync(path, snapshot.RawXml);

            _logger.Info($"Fundamental XML saved: {path}");

            _logger.Info(
                $"Snapshot summary: " +
                $"Ticker={snapshot.Ticker}, " +
                $"MarketCap={snapshot.MarketCap}, " +
                $"SharesOutstanding={snapshot.SharesOutstanding}, " +
                $"Sector={snapshot.Sector}, " +
                $"Industry={snapshot.Industry}");
        }
    }
}