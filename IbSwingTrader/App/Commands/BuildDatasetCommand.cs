using System.Collections.Concurrent;
using IBApi;

namespace IbSwingTrader.App.Commands
{
    public class BuildDatasetCommand : ICommand
    {
        private readonly ICsvTradeReader _csvTradeReader;
        private readonly ITradePositionMerger _tradePositionMerger;
        private readonly ITwsConnection _connection;
        private readonly ICsvWriter _csvWriter;
        private readonly IContractResolver _contractResolver;
        private readonly IHistoricalDataService _historicalService;
        private readonly ITradeDatasetBuilder _datasetBuilder;
        private readonly ITextLogger _logger;
        private readonly IAgentPathService _pathService;
        private readonly IFailedHistoryRequestTableFormatter _failedHistoryRequestTableFormatter;
        private readonly BuildDatasetSettings _buildDataset;
        private readonly SemaphoreSlim _semaphore;

        private readonly ConcurrentBag<FailedHistoryRequest> _failedRequests = new();

        public BuildDatasetCommand(
            ICsvTradeReader csvTradeReader,
            ITradePositionMerger tradePositionMerger,
            ITwsConnection connection,
            ICsvWriter csvWriter,
            IContractResolver contractResolver,
            IHistoricalDataService historicalService,
            ITradeDatasetBuilder datasetBuilder,
            ITextLogger logger,
            IAgentPathService pathService,
            IBuildDatasetSettingsProvider buildDatasetSettingsProvider,
            IFailedHistoryRequestTableFormatter failedHistoryRequestTableFormatter)
        {
            _csvTradeReader = csvTradeReader;
            _tradePositionMerger = tradePositionMerger;
            _connection = connection;
            _csvWriter = csvWriter;
            _contractResolver = contractResolver;
            _historicalService = historicalService;
            _datasetBuilder = datasetBuilder;
            _logger = logger;
            _pathService = pathService;
            _failedHistoryRequestTableFormatter = failedHistoryRequestTableFormatter;

            _buildDataset = buildDatasetSettingsProvider.Get();
            _semaphore = new SemaphoreSlim(_buildDataset.MaxParallelTickers);
        }

        public async Task RunAsync()
        {
            var tradesPath = _pathService.GetTradesFile();
            var datasetPath = _pathService.GetDatasetFile();

            var rawFills = _csvTradeReader.Read(tradesPath);

            if (rawFills.Count == 0)
            {
                _logger.Error("No trades found");
                return;
            }

            var trades = _tradePositionMerger.Merge(rawFills);

            _logger.Info($"Raw fills: {rawFills.Count} -> merged trades: {trades.Count}");

            if (trades.Count == 0)
            {
                _logger.Error("No trades left after merging fills");
                return;
            }

            var grouped = trades
                .GroupBy(t => t.Ticker)
                .OrderBy(g => g.Key)
                .ToList();

            _logger.Info($"Tickers found: {grouped.Count}");

            _connection.Connect();
            await _connection.Ready.Task;

            var tasks = grouped
                .Select(ProcessTicker)
                .ToList();

            var results = await Task.WhenAll(tasks);
            var allRows = results.SelectMany(x => x).ToList();

            _logger.EmptyLine();
            _logger.Info($"Total dataset rows: {allRows.Count}");

            _csvWriter.Write(datasetPath, allRows);

            _logger.Info($"Dataset saved: {datasetPath}");

            _logger.EmptyLine();
            _logger.Info($"Failed requests: {_failedRequests.Count}");

            _logger.EmptyLine();
            var failedTable = _failedHistoryRequestTableFormatter.Format(_failedRequests);
            _logger.InfoBlock("FAILED HISTORY REQUESTS", failedTable);
        }

        private async Task<List<TradeDatasetRow>> ProcessTicker(
            IGrouping<string, TradeRecord> tickerGroup)
        {
            await _semaphore.WaitAsync();

            try
            {
                var originalTicker = tickerGroup.Key.Trim().ToUpperInvariant();
                var requestTicker = MapTickerAlias(originalTicker);
                var tickerTrades = tickerGroup.ToList();

                if (!string.Equals(originalTicker, requestTicker, StringComparison.OrdinalIgnoreCase))
                    _logger.Info($"Ticker remapped: {originalTicker} → {requestTicker}");

                var earliest = tickerTrades.Min(t => t.EntryTimeMarket);
                var latest = tickerTrades.Max(t => t.ExitTimeMarket);

                var start = earliest.AddDays(-_buildDataset.HistoryWarmupDays);
                var end = latest.AddDays(_buildDataset.FuturePaddingDays);

                _logger.EmptyLine();
                _logger.Info($"Ticker: {originalTicker}");
                _logger.Info($"Trades: {tickerTrades.Count}");
                _logger.Info(
                    $"Range: {start:yyyy-MM-dd} -> {end:yyyy-MM-dd} " +
                    $"(last trade exit: {latest:yyyy-MM-dd})");

                Contract contract;

                try
                {
                    contract = await _contractResolver.ResolveStockAsync(requestTicker);

                    _logger.Info(
                        $"Resolved contract: {contract.Symbol} " +
                        $"conId={contract.ConId} " +
                        $"exchange={contract.Exchange}");
                }
                catch (Exception ex)
                {
                    RegisterFailure(originalTicker, $"Failed to resolve contract: {ex.Message}");
                    return [];
                }

                List<Candle>? candles;

                try
                {
                    candles = await _historicalService.GetCandlesRange(
                        requestTicker,
                        contract,
                        Timeframe.H4,
                        start,
                        end);
                }
                catch (Exception ex)
                {
                    RegisterFailure(originalTicker, $"Failed to load candles: {ex.Message}");
                    return [];
                }

                if (candles == null || candles.Count == 0)
                {
                    RegisterFailure(originalTicker, "No market data");
                    return [];
                }

                if (candles.Count < _buildDataset.MinimumCandlesRequired)
                {
                    RegisterFailure(originalTicker, $"Not enough candles: {candles.Count}");
                    return [];
                }

                try
                {
                    var rows = _datasetBuilder.Build(tickerTrades, candles);

                    if (rows.Count == 0)
                    {
                        RegisterFailure(originalTicker, "No dataset rows built");
                        return [];
                    }

                    _logger.Info($"Rows built for {originalTicker}: {rows.Count}");
                    return rows;
                }
                catch (Exception ex)
                {
                    RegisterFailure(originalTicker, $"Dataset build failed: {ex.Message}");
                    return [];
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void RegisterFailure(string ticker, string problem)
        {
            _logger.Info($"Skipping {ticker} — {problem}");

            _failedRequests.Add(new FailedHistoryRequest
            {
                Ticker = ticker,
                Problem = problem
            });
        }

        private string MapTickerAlias(string ticker)
        {
            if (_buildDataset.TickerAliases.TryGetValue(ticker, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped.Trim().ToUpperInvariant();
            }

            return ticker;
        }
    }
}
