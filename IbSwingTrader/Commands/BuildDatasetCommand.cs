using System.Collections.Concurrent;
using IBApi;
using IbSwingTrader.Infrastructure.Historical;
using IbSwingTrader.Interfaces;
using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.Models;

namespace IbSwingTrader.Commands
{
    public class BuildDatasetCommand(
        ITwsConnection connection,
        ICsvWriter csvWriter,
        IContractResolver contractResolver,
        IHistoricalDataService historicalService,
        ITradeDatasetBuilder datasetBuilder,
        ITextLogger logger) : ICommand
    {
        private readonly ITwsConnection _connection = connection;
        private readonly ICsvWriter _csvWriter = csvWriter;
        private readonly IContractResolver _contractResolver = contractResolver;
        private readonly IHistoricalDataService _historicalService = historicalService;
        private readonly ITradeDatasetBuilder _datasetBuilder = datasetBuilder;
        private readonly ITextLogger _logger = logger;

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(3);
        private readonly ConcurrentBag<FailedHistoryRequest> _failedRequests = new ();

        private string? _tradesPath = null;
        private string? _datasetPath = null;

        private Dictionary<string, string> TickerAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NYCB"] = "FLG"
        };

        public async Task RunAsync(params string[] args)
        {
            _tradesPath = args.Length > 0 ? args[0] : _tradesPath;
            _datasetPath = args.Length > 1 ? args[1] : _datasetPath;
            if (string.IsNullOrWhiteSpace(_tradesPath) || string.IsNullOrWhiteSpace(_datasetPath))
            {
                _logger.Error("Usage: BuildDatasetCommand <trades.csv> <dataset.csv>");
                return;
            }

            var trades = CsvTradeReader.Read(_tradesPath);

            if (trades.Count == 0)
            {
                _logger.Error("No trades found");
                return;
            }

            var grouped = trades
                .GroupBy(t => t.Ticker)
                .OrderBy(g => g.Key)
                .ToList();

            _logger.Info($"Tickers found: {grouped.Count}");
            _connection.Connect();
            await _connection.Ready.Task;

            var tasks = new List<Task<List<TradeDatasetRow>>>();

            foreach (var g in grouped)
                tasks.Add(ProcessTicker(g));

            var results = await Task.WhenAll(tasks);

            var allRows = results.SelectMany(r => r).ToList();

            _logger.EmptyLine();
            _logger.Info($"Total dataset rows: {allRows.Count}");
            _csvWriter.Write(_datasetPath, allRows);

            _logger.Info($"Dataset saved: {_datasetPath}");

            _logger.EmptyLine();
            _logger.Info($"Failed requests: {_failedRequests.Count}");

            _logger.EmptyLine();
            var failedTable = FailedHistoryRequestTableFormatter.Format(_failedRequests);
            _logger.InfoBlock("FAILED HISTORY REQUESTS", failedTable);

        }

        private async Task<List<TradeDatasetRow>> ProcessTicker(IGrouping<string, TradeRecord> tickerGroup)
        {
            await _semaphore.WaitAsync();

            try
            {
                var originalTicker = tickerGroup.Key.Trim().ToUpperInvariant();
                var requestTicker = originalTicker;

                if (TickerAliases.TryGetValue(originalTicker, out var mapped))
                {
                    _logger.Info($"Ticker remapped: {originalTicker} → {mapped}");
                    requestTicker = mapped;
                }

                var tickerTrades = tickerGroup.ToList();

                var earliest = tickerTrades.Min(t => t.EntryTimeUtc);
                var latest = tickerTrades.Max(t => t.ExitTimeUtc);

                // Слева запас под warmup / индикаторы
                var start = earliest.AddDays(-120);

                // Справа запас под future bars / target
                var end = latest.AddDays(21);

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
                    var problem = $"Failed to resolve contract: {ex.Message}";
                    _logger.Error($"{originalTicker}: {problem}");

                    _failedRequests.Add(new FailedHistoryRequest
                    {
                        Ticker = originalTicker,
                        Problem = problem
                    });

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
                    var problem = $"Failed to load candles: {ex.Message}";
                    _logger.Error($"{originalTicker}: {problem}");

                    _failedRequests.Add(new FailedHistoryRequest
                    {
                        Ticker = originalTicker,
                        Problem = problem
                    });

                    return [];
                }

                if (candles == null || candles.Count == 0)
                {
                    var problem = "No market data";
                    _logger.Info($"Skipping {originalTicker} — {problem}");

                    _failedRequests.Add(new FailedHistoryRequest
                    {
                        Ticker = originalTicker,
                        Problem = problem
                    });

                    return [];
                }

                if (candles.Count < 60)
                {
                    var problem = $"Not enough candles: {candles.Count}";
                    _logger.Info($"Skipping {originalTicker} — {problem}");

                    _failedRequests.Add(new FailedHistoryRequest
                    {
                        Ticker = originalTicker,
                        Problem = problem
                    });

                    return [];
                }

                List<TradeDatasetRow> rows;

                try
                {
                    rows = _datasetBuilder.Build(tickerTrades, candles);

                    if (rows.Count == 0)
                    {
                        var problem = "No dataset rows built";

                        _logger.Info($"Skipping {originalTicker} — {problem}");

                        _failedRequests.Add(new FailedHistoryRequest
                        {
                            Ticker = originalTicker,
                            Problem = problem
                        });

                        return [];
                    }
                }
                catch (Exception ex)
                {
                    var problem = $"Dataset build failed: {ex.Message}";
                    _logger.Error($"{originalTicker}: {problem}");

                    _failedRequests.Add(new FailedHistoryRequest
                    {
                        Ticker = originalTicker,
                        Problem = problem
                    });

                    return [];
                }

                _logger.Info($"Rows built for {originalTicker}: {rows.Count}");

                return rows;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
