using System.Collections.Concurrent;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class BuildResearchDatasetCommand(
        IJsonFileService jsonFileService,
        ICsvWriter csvWriter,
        ITwsConnection connection,
        IContractResolver contractResolver,
        IHistoricalDataService historicalService,
        IFeatureEngine featureEngine,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider,
        IResearchSettingsProvider researchSettingsProvider,
        IFailedHistoryRequestTableFormatter failedHistoryRequestTableFormatter) : ICommand
    {
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ICsvWriter _csvWriter = csvWriter;
        private readonly ITwsConnection _connection = connection;
        private readonly IContractResolver _contractResolver = contractResolver;
        private readonly IHistoricalDataService _historicalService = historicalService;
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;
        private readonly IResearchSettingsProvider _researchSettingsProvider = researchSettingsProvider;
        private readonly IFailedHistoryRequestTableFormatter _failedHistoryRequestTableFormatter = failedHistoryRequestTableFormatter;

        private readonly ConcurrentBag<FailedHistoryRequest> _failedRequests = [];

        public async Task RunAsync()
        {
            var settings = _researchSettingsProvider.Get();
            var twsSettings = _twsSettingsProvider.Get();
            var outputPath = Path.GetFullPath(Path.Combine(_pathService.GetDataRoot(), settings.OutputFile));

            if (!string.Equals(settings.Mode, "top_gainers", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported research mode: {settings.Mode}");

            if (!string.Equals(settings.Source, "known_tickers", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported research source: {settings.Source}");

            var tickers = await LoadKnownTickersAsync();

            _logger.Info(
                $"Research settings: Mode={settings.Mode}, Source={settings.Source}, LookbackCalendarDays={settings.LookbackCalendarDays}, MinimumCandles={settings.MinimumCandles}, MinRunupPct={settings.MinRunupPct}, MaxBarsToPeak={settings.MaxBarsToPeak}");
            _logger.Info($"Research tickers found: {tickers.Count}");

            if (tickers.Count == 0)
            {
                _logger.Error("No research tickers found.");
                return;
            }

            EnsureConnected(twsSettings.ConnectTimeoutSeconds);

            var semaphore = new SemaphoreSlim(settings.MaxParallelTickers);
            var tasks = tickers.Select(async ticker =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await ProcessTickerAsync(ticker, settings);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            var allRows = results.SelectMany(x => x).ToList();

            _csvWriter.Write(outputPath, allRows);

            _logger.EmptyLine();
            _logger.Info($"Total research dataset rows: {allRows.Count}");
            _logger.Info($"Research dataset saved: {outputPath}");

            _logger.EmptyLine();
            _logger.Info($"Failed requests: {_failedRequests.Count}");
            _logger.EmptyLine();
            _logger.InfoBlock("FAILED HISTORY REQUESTS", _failedHistoryRequestTableFormatter.Format(_failedRequests));
        }

        private async Task<List<string>> LoadKnownTickersAsync()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var candidatesPath = _pathService.GetCandidatesFile();
            if (File.Exists(candidatesPath))
            {
                var json = await File.ReadAllTextAsync(candidatesPath);
                var first = json.FirstOrDefault(x => !char.IsWhiteSpace(x));

                if (first == '[')
                {
                    var candidates = await _jsonFileService.ReadAsync<List<CandidateDetails>>(candidatesPath) ?? [];
                    foreach (var candidate in candidates)
                        result.Add(candidate.Ticker);
                }
                else
                {
                    var document = await _jsonFileService.ReadAsync<CandidateFileDocument>(candidatesPath);
                    foreach (var candidate in document?.Candidates ?? [])
                        result.Add(candidate.Ticker);
                }
            }

            var wishListPath = _pathService.GetWishListFile();
            var wishList = await _jsonFileService.ReadAsync<List<WishListItem>>(wishListPath) ?? [];
            foreach (var item in wishList)
                result.Add(item.Ticker);

            var evaluationsPath = _pathService.GetEvaluationsFile();
            if (File.Exists(evaluationsPath))
            {
                var lines = await File.ReadAllLinesAsync(evaluationsPath);
                if (lines.Length > 1)
                {
                    var headers = lines[0].Split(';');
                    var tickerIndex = Array.FindIndex(headers, x => string.Equals(x, "Ticker", StringComparison.OrdinalIgnoreCase));

                    if (tickerIndex >= 0)
                    {
                        foreach (var line in lines.Skip(1))
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            var parts = line.Split(';');
                            if (parts.Length > tickerIndex && !string.IsNullOrWhiteSpace(parts[tickerIndex]))
                                result.Add(parts[tickerIndex].Trim());
                        }
                    }
                }
            }

            return result
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<ResearchDatasetRow>> ProcessTickerAsync(string ticker, ResearchSettings settings)
        {
            try
            {
                var contract = await _contractResolver.ResolveStockAsync(ticker);

                var end = MarketTime.Now();
                var start = end.AddDays(-settings.LookbackCalendarDays);

                var candles = await _historicalService.GetCandlesRange(
                    ticker,
                    contract,
                    Timeframe.H4,
                    start,
                    end);

                if (candles == null || candles.Count < settings.MinimumCandles)
                {
                    RegisterFailure(ticker, $"Not enough candles: {candles?.Count ?? 0}");
                    return [];
                }

                var rows = BuildTopGainerRows(ticker, settings, candles);
                _logger.Info($"Research rows built for {ticker}: {rows.Count}");
                return rows;
            }
            catch (Exception ex)
            {
                RegisterFailure(ticker, ex.Message);
                return [];
            }
        }

        private List<ResearchDatasetRow> BuildTopGainerRows(
            string ticker,
            ResearchSettings settings,
            List<Candle> candles)
        {
            var rows = new List<ResearchDatasetRow>();

            if (candles.Count < settings.MinimumCandles)
                return rows;

            var i = settings.LocalExtremaLookbackBars;

            while (i < candles.Count - settings.LocalExtremaLookbackBars - 1)
            {
                if (!IsLocalMinimum(candles, i, settings.LocalExtremaLookbackBars))
                {
                    i++;
                    continue;
                }

                var maxForwardIndex = Math.Min(candles.Count - 1, i + settings.MaxBarsToPeak);
                var peakIndex = i;
                var peakHigh = candles[i].High;
                var minLowAfterEntry = candles[i].Low;

                for (var j = i + 1; j <= maxForwardIndex; j++)
                {
                    if (candles[j].High > peakHigh)
                    {
                        peakHigh = candles[j].High;
                        peakIndex = j;
                    }

                    if (candles[j].Low < minLowAfterEntry)
                        minLowAfterEntry = candles[j].Low;
                }

                var referencePrice = candles[i].Close;
                if (referencePrice <= 0m || peakIndex <= i)
                {
                    i++;
                    continue;
                }

                var runupPct = (peakHigh - referencePrice) / referencePrice * 100m;
                if (runupPct < settings.MinRunupPct)
                {
                    i++;
                    continue;
                }

                rows.Add(BuildRow(
                    ticker,
                    settings,
                    candles,
                    i,
                    peakIndex,
                    peakHigh,
                    minLowAfterEntry));

                i = peakIndex + 1;
            }

            return rows;
        }

        private ResearchDatasetRow BuildRow(
            string ticker,
            ResearchSettings settings,
            List<Candle> candles,
            int referenceIndex,
            int peakIndex,
            decimal peakHigh,
            decimal minLowAfterReference)
        {
            var referenceFeatures = _featureEngine.Calculate(candles, referenceIndex + 1);
            var referencePrice = candles[referenceIndex].Close;

            var row = new ResearchDatasetRow
            {
                Ticker = ticker,
                Mode = settings.Mode,
                Source = settings.Source,
                ReferenceType = "OracleBottom",
                ReferenceTimeMarket = candles[referenceIndex].Time,
                ReferencePrice = referencePrice,
                PeakTimeMarket = candles[peakIndex].Time,
                PeakPrice = peakHigh,
                RunupPct = referencePrice == 0m ? 0m : (peakHigh - referencePrice) / referencePrice * 100m,
                BarsToPeak = peakIndex - referenceIndex,
                MaxDrawdownBeforePeakPct = referencePrice == 0m ? 0m : (minLowAfterReference - referencePrice) / referencePrice * 100m,
                DistanceTo20dHigh = referenceFeatures.DistanceTo20dHigh,
                DistanceTo52wHigh = referenceFeatures.DistanceTo52wHigh
            };

            FillSeries(row, candles, referenceIndex, peakIndex);
            return row;
        }

        private void FillSeries(
            ResearchDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            FillDailySeries(row, candles, entryIndex, exitIndex);
            FillWeeklySeries(row, candles, entryIndex, exitIndex);
            FillH4Series(row, candles, entryIndex, exitIndex);
        }

        private void FillDailySeries(
            ResearchDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            DateTime? lastDay = null;

            for (var i = entryIndex; i <= exitIndex; i++)
            {
                var day = candles[i].Time.Date;

                if (lastDay.HasValue && lastDay.Value == day)
                    continue;

                lastDay = day;

                var lastBarIndexOfDay = i;
                while (lastBarIndexOfDay + 1 <= exitIndex &&
                       candles[lastBarIndexOfDay + 1].Time.Date == day)
                {
                    lastBarIndexOfDay++;
                }

                var features = _featureEngine.Calculate(candles, lastBarIndexOfDay + 1);
                row.DailyMaDistances.Add(features.DailyMaSignedDistancePct);
                row.DailyRsiValues.Add(features.DailyRSI14);
                row.DailyMacdValues.Add(features.DailyMACDLineMinusSignal);
            }
        }

        private void FillWeeklySeries(
            ResearchDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            DateTime? lastWeekStart = null;

            for (var i = entryIndex; i <= exitIndex; i++)
            {
                var weekStart = GetWeekStart(candles[i].Time);

                if (lastWeekStart.HasValue && lastWeekStart.Value == weekStart)
                    continue;

                lastWeekStart = weekStart;

                var lastBarIndexOfWeek = i;
                while (lastBarIndexOfWeek + 1 <= exitIndex)
                {
                    var nextWeekStart = GetWeekStart(candles[lastBarIndexOfWeek + 1].Time);
                    if (nextWeekStart != weekStart)
                        break;
                    lastBarIndexOfWeek++;
                }

                var features = _featureEngine.Calculate(candles, lastBarIndexOfWeek + 1);

                if (features.WeeklyMaSignedDistancePct.HasValue)
                    row.WeeklyMaDistances.Add(features.WeeklyMaSignedDistancePct.Value);

                if (features.WeeklyRSI14.HasValue)
                    row.WeeklyRsiValues.Add(features.WeeklyRSI14.Value);

                if (features.WeeklyMACDLineMinusSignal.HasValue)
                    row.WeeklyMacdValues.Add(features.WeeklyMACDLineMinusSignal.Value);
            }
        }

        private void FillH4Series(
            ResearchDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            row.H4MaDistances = [];
            row.H4RsiValues = [];
            row.H4MacdValues = [];

            for (var i = entryIndex; i <= exitIndex; i++)
            {
                var features = _featureEngine.Calculate(candles, i + 1);
                row.H4MaDistances.Add(features.H4MaSignedDistancePct);
                row.H4RsiValues.Add(features.RSI14);
                row.H4MacdValues.Add(features.MACDLineMinusSignal);
            }
        }

        private static bool IsLocalMinimum(List<Candle> candles, int index, int radius)
        {
            var low = candles[index].Low;

            for (var i = Math.Max(0, index - radius); i <= Math.Min(candles.Count - 1, index + radius); i++)
            {
                if (i == index)
                    continue;

                if (candles[i].Low <= low)
                    return false;
            }

            return true;
        }

        private void EnsureConnected(int timeoutSeconds)
        {
            if (_connection.IsConnected)
                return;

            _logger.Info("Connecting to TWS...");
            _connection.Connect();

            var connected = _connection.Ready.Task.Wait(TimeSpan.FromSeconds(timeoutSeconds));

            if (!connected || !_connection.IsConnected)
                throw new InvalidOperationException("Failed to connect to TWS.");

            _logger.Info("TWS connected.");
        }

        private static DateTime GetWeekStart(DateTime value)
        {
            var offset = ((int)value.DayOfWeek + 6) % 7;
            return value.Date.AddDays(-offset);
        }

        private void RegisterFailure(string ticker, string problem)
        {
            _logger.Info($"Skipping {ticker} - {problem}");
            _failedRequests.Add(new FailedHistoryRequest
            {
                Ticker = ticker,
                Problem = problem
            });
        }
    }
}
