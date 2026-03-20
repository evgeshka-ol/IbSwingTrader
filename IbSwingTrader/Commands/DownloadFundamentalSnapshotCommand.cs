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

            foreach (var ticker in tickers)
            {
                try
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
                catch (Exception ex)
                {
                    _logger.Error($"Error processing {ticker}: {ex.Message}");
                }
            }
        }
    }
}