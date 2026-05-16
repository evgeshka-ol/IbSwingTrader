using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class BuildResearchDatasetCommand(
        IJsonFileService jsonFileService,
        ICsvWriter csvWriter,
        IEvaluationDatasetCsvService evaluationDatasetCsvService,
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
        private readonly IEvaluationDatasetCsvService _evaluationDatasetCsvService = evaluationDatasetCsvService;
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
        private const int RecentDailySeriesLength = 12;
        private const int RecentWeeklySeriesLength = 10;
        private const int RecentH4SeriesLength = 16;

        public async Task RunAsync()
        {
            var settings = _researchSettingsProvider.Get();
            var twsSettings = _twsSettingsProvider.Get();
            var outputPath = Path.GetFullPath(Path.Combine(_pathService.GetDataRoot(), settings.OutputFile));
            var recentScanCutoff = GetRecentScanCutoff(settings);

            if (!string.Equals(settings.Mode, "top_gainers", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported research mode: {settings.Mode}");

            if (!string.Equals(settings.Source, "known_tickers", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported research source: {settings.Source}");

            var tickers = await LoadKnownTickersAsync();

            _logger.Info(
                $"Research settings: Mode={settings.Mode}, Source={settings.Source}, LookbackCalendarDays={settings.LookbackCalendarDays}, MinimumCandles={settings.MinimumCandles}, MinRunupPct={settings.MinRunupPct}, MaxParallelTickers={settings.MaxParallelTickers}");
            if (settings.RecentScanDays.HasValue)
                _logger.Info($"Research RecentScanDays filter: {settings.RecentScanDays.Value}");
            if (settings.RecentEvaluationScanDays.HasValue)
                _logger.Info($"Research RecentEvaluationScanDays filter: {settings.RecentEvaluationScanDays.Value}");
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
            var freshRows = results
                .SelectMany(x => x)
                .Select(MapToCsvRow)
                .ToList();
            var existingRows = await ReadExistingRowsAsync(outputPath);
            if (recentScanCutoff.HasValue)
            {
                existingRows = existingRows
                    .Where(x => x.ScanTime >= recentScanCutoff.Value)
                    .ToList();
            }
            var allRows = existingRows
                .Concat(freshRows)
                .GroupBy(BuildResearchRowKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Aggregate(ChooseBetterResearchRow))
                .Where(HasUsefulSeries)
                .OrderBy(x => x, Comparer<ResearchTopGainerDatasetRow>.Create((left, right) => CompareRows(left, right, settings)))
                .ToList();

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
            var settings = _researchSettingsProvider.Get();
            var recentScanCutoff = GetRecentScanCutoff(settings);
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

            var evaluationDatasetPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));
            if (File.Exists(evaluationDatasetPath))
            {
                var datasetRows = await _evaluationDatasetCsvService.ReadAsync(evaluationDatasetPath);
                var recentEvaluationCutoff = settings.RecentEvaluationScanDays.HasValue && settings.RecentEvaluationScanDays.Value > 0
                    ? MarketTime.Now().Date.AddDays(-settings.RecentEvaluationScanDays.Value)
                    : (DateTime?)null;

                foreach (var row in datasetRows)
                {
                    if (string.IsNullOrWhiteSpace(row.Ticker))
                        continue;

                    if (recentScanCutoff.HasValue &&
                        row.ScanTime < recentScanCutoff.Value)
                    {
                        continue;
                    }

                    if (recentEvaluationCutoff.HasValue &&
                        row.ScanTime < recentEvaluationCutoff.Value)
                    {
                        continue;
                    }

                    result.Add(row.Ticker.Trim());
                }
            }

            return [.. result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
        }

        private static DateTime? GetRecentScanCutoff(ResearchSettings settings)
        {
            if (!settings.RecentScanDays.HasValue || settings.RecentScanDays.Value <= 0)
                return null;

            var normalizedDays = Math.Max(1, settings.RecentScanDays.Value);
            return MarketTime.Now().Date.AddDays(-(normalizedDays - 1));
        }

        private static string BuildResearchRowKey(ResearchTopGainerDatasetRow row)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Ticker}|{row.ScanTime:yyyy-MM-dd HH:mm:ss}");
        }

        private static async Task<List<ResearchTopGainerDatasetRow>> ReadExistingRowsAsync(string path)
        {
            if (!File.Exists(path))
                return [];

            var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);
            if (lines.Length <= 1)
                return [];

            var headers = SplitCsvLine(lines[0]);
            var headerIndex = headers
                .Select((name, index) => new { name, index })
                .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

            var properties = typeof(ResearchTopGainerDatasetRow)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanWrite)
                .ToArray();

            var rows = new List<ResearchTopGainerDatasetRow>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = SplitCsvLine(line);
                var row = new ResearchTopGainerDatasetRow
                {
                    Ticker = string.Empty
                };

                foreach (var property in properties)
                {
                    if (!TryGetResearchColumnIndex(headerIndex, property.Name, out var index))
                        continue;

                    if (index >= values.Count)
                        continue;

                    var parsed = ParseValue(property.PropertyType, values[index]);
                    property.SetValue(row, parsed);
                }

                if (string.IsNullOrWhiteSpace(row.Ticker))
                    continue;

                row.NegativePotentialPct = Math.Abs(row.NegativePotentialPct);
                row.PositivePotentialPct = row.PositivePotentialPct == 0m ? row.AmplitudePct : row.PositivePotentialPct;

                rows.Add(row);
            }

            return rows;
        }

        private static ResearchTopGainerDatasetRow ChooseBetterResearchRow(
            ResearchTopGainerDatasetRow left,
            ResearchTopGainerDatasetRow right)
        {
            var leftScore = GetResearchRowCompletenessScore(left);
            var rightScore = GetResearchRowCompletenessScore(right);

            if (leftScore != rightScore)
                return rightScore > leftScore ? right : left;

            if (left.AmplitudePct != right.AmplitudePct)
                return right.AmplitudePct > left.AmplitudePct ? right : left;

            if (left.MaxTime != right.MaxTime)
                return right.MaxTime > left.MaxTime ? right : left;

            return right;
        }

        private static int GetResearchRowCompletenessScore(ResearchTopGainerDatasetRow row)
        {
            var score = 0;

            score += row.DailyBbMidDistanceSeries?.Count ?? 0;
            score += row.DailyBbUpperDistanceSeries?.Count ?? 0;
            score += row.DailyBbWidthSeries?.Count ?? 0;
            score += row.WeeklyBbMidDistanceSeries?.Count ?? 0;
            score += row.WeeklyBbUpperDistanceSeries?.Count ?? 0;
            score += row.WeeklyBbWidthSeries?.Count ?? 0;
            score += row.H4BbMidDistanceSeries?.Count ?? 0;
            score += row.H4BbUpperDistanceSeries?.Count ?? 0;
            score += row.H4BbWidthSeries?.Count ?? 0;
            score += row.DailyRsiSeries?.Count ?? 0;
            score += row.DailyMacdSeries?.Count ?? 0;
            score += row.WeeklyRsiSeries?.Count ?? 0;
            score += row.WeeklyMacdSeries?.Count ?? 0;
            score += row.H4RsiSeries?.Count ?? 0;
            score += row.H4MacdSeries?.Count ?? 0;

            return score;
        }

        private static bool HasUsefulSeries(ResearchTopGainerDatasetRow row)
        {
            return GetResearchRowCompletenessScore(row) > 0;
        }

        private static object? ParseValue(Type type, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (Nullable.GetUnderlyingType(type) != null)
                    return null;

                if (type == typeof(string))
                    return string.Empty;

                if (type == typeof(List<decimal>))
                    return new List<decimal>();

                return Activator.CreateInstance(type);
            }

            var targetType = Nullable.GetUnderlyingType(type) ?? type;

            if (targetType == typeof(string))
                return raw;

            if (targetType == typeof(DateTime))
            {
                if (DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt;

                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                    return dt;

                return Nullable.GetUnderlyingType(type) != null ? null : default(DateTime);
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    return dec;

                return Nullable.GetUnderlyingType(type) != null ? null : 0m;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                    return i;

                return 0;
            }

            if (targetType == typeof(List<decimal>))
                return ParseDecimalList(raw);

            return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
        }

        private static List<decimal> ParseDecimalList(string raw)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length < 2 || trimmed == "[]")
                return [];

            if (trimmed[0] == '[' && trimmed[^1] == ']')
                trimmed = trimmed[1..^1];

            if (string.IsNullOrWhiteSpace(trimmed))
                return [];

            return [.. trimmed
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => decimal.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) ? dec : 0m)];
        }

        private static bool TryGetResearchColumnIndex(
            Dictionary<string, int> headerIndex,
            string propertyName,
            out int index)
        {
            if (headerIndex.TryGetValue(propertyName, out index))
                return true;

            foreach (var alias in GetResearchColumnAliases(propertyName))
            {
                if (headerIndex.TryGetValue(alias, out index))
                    return true;
            }

            index = -1;
            return false;
        }

        private static List<string> GetResearchColumnAliases(string propertyName)
        {
            return propertyName switch
            {
                nameof(ResearchTopGainerDatasetRow.ScanTime) => ["ReferenceTime"],
                nameof(ResearchTopGainerDatasetRow.ScanPrice) => ["ReferencePrice"],
                nameof(ResearchTopGainerDatasetRow.MaxTime) => ["PeakTime"],
                nameof(ResearchTopGainerDatasetRow.MaxPrice) => ["PeakPrice"],
                nameof(ResearchTopGainerDatasetRow.AmplitudePct) => ["RunupPct"],
                nameof(ResearchTopGainerDatasetRow.PositivePotentialPct) => ["RunupPct"],
                nameof(ResearchTopGainerDatasetRow.NegativePotentialPct) => ["MaxDrawdownBeforePeakPct"],
                nameof(ResearchTopGainerDatasetRow.BarsToMax) => ["BarsToPeak"],
                nameof(ResearchTopGainerDatasetRow.DailyMaSeries) => ["DailyMaDistances"],
                nameof(ResearchTopGainerDatasetRow.DailyBbMidDistanceSeries) => ["DailyBollingerMidDistances"],
                nameof(ResearchTopGainerDatasetRow.DailyBbUpperDistanceSeries) => ["DailyBollingerUpperDistances"],
                nameof(ResearchTopGainerDatasetRow.DailyBbWidthSeries) => ["DailyBollingerBandWidths"],
                nameof(ResearchTopGainerDatasetRow.DailyRsiSeries) => ["DailyRsiValues"],
                nameof(ResearchTopGainerDatasetRow.DailyMacdSeries) => ["DailyMacdValues"],
                nameof(ResearchTopGainerDatasetRow.WeeklyMaSeries) => ["WeeklyMaDistances"],
                nameof(ResearchTopGainerDatasetRow.WeeklyBbMidDistanceSeries) => ["WeeklyBollingerMidDistances"],
                nameof(ResearchTopGainerDatasetRow.WeeklyBbUpperDistanceSeries) => ["WeeklyBollingerUpperDistances"],
                nameof(ResearchTopGainerDatasetRow.WeeklyBbWidthSeries) => ["WeeklyBollingerBandWidths"],
                nameof(ResearchTopGainerDatasetRow.WeeklyRsiSeries) => ["WeeklyRsiValues"],
                nameof(ResearchTopGainerDatasetRow.WeeklyMacdSeries) => ["WeeklyMacdValues"],
                nameof(ResearchTopGainerDatasetRow.H4MaSeries) => ["H4MaDistances"],
                nameof(ResearchTopGainerDatasetRow.H4BbMidDistanceSeries) => ["H4BollingerMidDistances"],
                nameof(ResearchTopGainerDatasetRow.H4BbUpperDistanceSeries) => ["H4BollingerUpperDistances"],
                nameof(ResearchTopGainerDatasetRow.H4BbWidthSeries) => ["H4BollingerBandWidths"],
                nameof(ResearchTopGainerDatasetRow.H4RsiSeries) => ["H4RsiValues"],
                nameof(ResearchTopGainerDatasetRow.H4MacdSeries) => ["H4MacdValues"],
                _ => []
            };
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
        }

        private static char DetectDelimiter(string line)
        {
            var commaCount = 0;
            var semicolonCount = 0;
            var inQuotes = false;

            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (inQuotes)
                    continue;

                if (c == ',')
                    commaCount++;
                else if (c == ';')
                    semicolonCount++;
            }

            return commaCount >= semicolonCount ? ',' : ';';
        }

        private static List<string> SplitCsvLine(string line, char delimiter)
        {
            if (delimiter == ',')
                return SplitCsvLine(line);

            var result = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == delimiter && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
        }

        private async Task<List<ResearchDatasetRow>> ProcessTickerAsync(string ticker, ResearchSettings settings)
        {
            try
            {
                var resolveTimeout = TimeSpan.FromSeconds(
                    Math.Max(15, settings.ContractResolveTimeoutSeconds));

                var contract = await _contractResolver.ResolveStockAsync(ticker, resolveTimeout);

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

        private static ResearchTopGainerDatasetRow MapToCsvRow(ResearchDatasetRow row)
        {
            return new ResearchTopGainerDatasetRow
            {
                Ticker = row.Ticker,
                ScanTime = row.ReferenceTime,
                ScanPrice = row.ReferencePrice,
                MaxTime = row.PeakTime,
                MaxPrice = row.PeakPrice,
                AmplitudePct = row.RunupPct,
                PositivePotentialPct = row.RunupPct,
                NegativePotentialPct = Math.Abs(row.MaxDrawdownBeforePeakPct),
                BarsToMax = row.BarsToPeak,
                DistanceTo20dHigh = row.DistanceTo20dHigh,
                DistanceTo52wHigh = row.DistanceTo52wHigh,
                H4MaSignedDistancePct = row.H4MaSignedDistancePct,
                H4Rsi14 = row.H4Rsi14,
                H4MacdLineMinusSignal = row.H4MacdLineMinusSignal,
                DailyMaSignedDistancePct = row.DailyMaSignedDistancePct,
                DailyRsi14 = row.DailyRsi14,
                DailyMacdLineMinusSignal = row.DailyMacdLineMinusSignal,
                WeeklyMaSignedDistancePct = row.WeeklyMaSignedDistancePct,
                WeeklyRsi14 = row.WeeklyRsi14,
                WeeklyMacdLineMinusSignal = row.WeeklyMacdLineMinusSignal,
                DailyBollingerUpperDistancePct = row.DailyBollingerUpperDistancePct,
                DailyBollingerBandWidthPct = row.DailyBollingerBandWidthPct,
                WeeklyBollingerUpperDistancePct = row.WeeklyBollingerUpperDistancePct,
                WeeklyBollingerBandWidthPct = row.WeeklyBollingerBandWidthPct,
                Pullback10d = row.Pullback10d,
                DailyPullback10d = row.DailyPullback10d,
                VolumeRatio20 = row.VolumeRatio20,
                AtrRatio = row.AtrRatio,
                TrendPosition = row.TrendPosition,
                DailyTrendPosition = row.DailyTrendPosition,
                BbMidSignedDistancePct = row.BbMidSignedDistancePct,
                WeeklyMacdHistDelta = row.WeeklyMacdHistDelta,
                DailyMaSeries = [.. row.DailyMaDistances],
                DailyBbMidDistanceSeries = [.. row.DailyBollingerMidDistances],
                DailyBbUpperDistanceSeries = [.. row.DailyBollingerUpperDistances],
                DailyBbWidthSeries = [.. row.DailyBollingerBandWidths],
                DailyRsiSeries = [.. row.DailyRsiValues],
                DailyMacdSeries = [.. row.DailyMacdValues],
                WeeklyMaSeries = [.. row.WeeklyMaDistances],
                WeeklyBbMidDistanceSeries = [.. row.WeeklyBollingerMidDistances],
                WeeklyBbUpperDistanceSeries = [.. row.WeeklyBollingerUpperDistances],
                WeeklyBbWidthSeries = [.. row.WeeklyBollingerBandWidths],
                WeeklyRsiSeries = [.. row.WeeklyRsiValues],
                WeeklyMacdSeries = [.. row.WeeklyMacdValues],
                H4MaSeries = row.H4MaDistances == null ? null : [.. row.H4MaDistances],
                H4BbMidDistanceSeries = row.H4BollingerMidDistances == null ? null : [.. row.H4BollingerMidDistances],
                H4BbUpperDistanceSeries = row.H4BollingerUpperDistances == null ? null : [.. row.H4BollingerUpperDistances],
                H4BbWidthSeries = row.H4BollingerBandWidths == null ? null : [.. row.H4BollingerBandWidths],
                H4RsiSeries = row.H4RsiValues == null ? null : [.. row.H4RsiValues],
                H4MacdSeries = row.H4MacdValues == null ? null : [.. row.H4MacdValues]
            };
        }

        private static int CompareRows(
            ResearchTopGainerDatasetRow left,
            ResearchTopGainerDatasetRow right,
            ResearchSettings settings)
        {
            foreach (var sortColumn in settings.SortColumns ?? [])
            {
                var comparison = CompareByColumn(left, right, sortColumn);
                if (comparison != 0)
                    return comparison;
            }

            return string.Compare(left.Ticker, right.Ticker, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareByColumn(
            ResearchTopGainerDatasetRow left,
            ResearchTopGainerDatasetRow right,
            ResearchSortColumnSettings sortColumn)
        {
            if (string.IsNullOrWhiteSpace(sortColumn.Column))
                return 0;

            var orderedValues = sortColumn.OrderedValues ?? [];
            var comparison = sortColumn.Column switch
            {
                nameof(ResearchTopGainerDatasetRow.ScanTime) => left.ScanTime.CompareTo(right.ScanTime),
                nameof(ResearchTopGainerDatasetRow.MaxTime) => left.MaxTime.CompareTo(right.MaxTime),
                nameof(ResearchTopGainerDatasetRow.AmplitudePct) => left.AmplitudePct.CompareTo(right.AmplitudePct),
                nameof(ResearchTopGainerDatasetRow.PositivePotentialPct) => left.PositivePotentialPct.CompareTo(right.PositivePotentialPct),
                nameof(ResearchTopGainerDatasetRow.NegativePotentialPct) => left.NegativePotentialPct.CompareTo(right.NegativePotentialPct),
                nameof(ResearchTopGainerDatasetRow.BarsToMax) => left.BarsToMax.CompareTo(right.BarsToMax),
                nameof(ResearchTopGainerDatasetRow.Ticker) => CompareString(left.Ticker, right.Ticker, orderedValues),
                _ => 0
            };

            if (comparison == 0)
                return 0;

            return sortColumn.Descending ? -comparison : comparison;
        }

        private static int CompareString(
            string? left,
            string? right,
            List<string> orderedValues)
        {
            var leftValue = left ?? string.Empty;
            var rightValue = right ?? string.Empty;

            if (orderedValues.Count > 0)
            {
                var leftIndex = GetOrderedValueIndex(leftValue, orderedValues);
                var rightIndex = GetOrderedValueIndex(rightValue, orderedValues);
                var orderedComparison = leftIndex.CompareTo(rightIndex);
                if (orderedComparison != 0)
                    return orderedComparison;
            }

            return string.Compare(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetOrderedValueIndex(string value, List<string> orderedValues)
        {
            for (var i = 0; i < orderedValues.Count; i++)
            {
                if (string.Equals(orderedValues[i], value, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return int.MaxValue;
        }

        private List<ResearchDatasetRow> BuildTopGainerRows(
            string ticker,
            ResearchSettings settings,
            List<Candle> candles)
        {
            if (candles.Count < settings.MinimumCandles)
                return [];

            var latestIndex = candles.Count - 1;
            var sessionDate = candles[latestIndex].Time.Date;
            var sessionStartIndex = candles.FindIndex(x => x.Time.Date == sessionDate);
            if (sessionStartIndex <= 0)
                return [];

            var previousClose = candles[sessionStartIndex - 1].Close;
            var currentClose = candles[latestIndex].Close;
            if (previousClose <= 0m || currentClose <= 0m)
                return [];

            var peakIndex = sessionStartIndex;
            var peakHigh = candles[sessionStartIndex].High;
            var minLow = candles[sessionStartIndex].Low;

            for (var i = sessionStartIndex + 1; i <= latestIndex; i++)
            {
                if (candles[i].High > peakHigh)
                {
                    peakHigh = candles[i].High;
                    peakIndex = i;
                }

                if (candles[i].Low < minLow)
                    minLow = candles[i].Low;
            }

            var peakGainPct = (peakHigh - previousClose) / previousClose * 100m;
            if (peakGainPct < settings.MinRunupPct)
                return [];

            var drawdownPct = (minLow - previousClose) / previousClose * 100m;
            var featureCache = new Dictionary<int, FeatureSet>();

            return
            [
                BuildSessionSnapshotRow(
                    ticker,
                    settings,
                    candles,
                    latestIndex,
                    sessionStartIndex,
                    peakIndex,
                    peakGainPct,
                    drawdownPct,
                    featureCache)
            ];
        }

        private ResearchDatasetRow BuildSessionSnapshotRow(
            string ticker,
            ResearchSettings settings,
            List<Candle> candles,
            int latestIndex,
            int sessionStartIndex,
            int peakIndex,
            decimal peakGainPct,
            decimal drawdownPct,
            Dictionary<int, FeatureSet> featureCache)
        {
            var snapshotFeatures = GetFeatures(candles, latestIndex, featureCache);
            var snapshotPrice = candles[latestIndex].Close;
            var snapshotCandles = candles.Take(latestIndex + 1).ToList();

            var row = new ResearchDatasetRow
            {
                Ticker = ticker,
                Mode = settings.Mode,
                Source = settings.Source,
                ReferenceType = "SessionTopGainer",
                ReferenceTime = candles[latestIndex].Time,
                ReferencePrice = snapshotPrice,
                PeakTime = candles[peakIndex].Time,
                PeakPrice = candles[peakIndex].High,
                RunupPct = peakGainPct,
                BarsToPeak = peakIndex - sessionStartIndex,
                MaxDrawdownBeforePeakPct = drawdownPct,
                DistanceTo20dHigh = snapshotFeatures.DistanceTo20dHigh,
                DistanceTo52wHigh = snapshotFeatures.DistanceTo52wHigh,
                H4MaSignedDistancePct = snapshotFeatures.H4MaSignedDistancePct,
                H4Rsi14 = snapshotFeatures.RSI14,
                H4MacdLineMinusSignal = snapshotFeatures.MACDLineMinusSignal,
                DailyMaSignedDistancePct = snapshotFeatures.DailyMaSignedDistancePct,
                DailyRsi14 = snapshotFeatures.DailyRSI14,
                DailyMacdLineMinusSignal = snapshotFeatures.DailyMACDLineMinusSignal,
                WeeklyMaSignedDistancePct = snapshotFeatures.WeeklyMaSignedDistancePct,
                WeeklyRsi14 = snapshotFeatures.WeeklyRSI14,
                WeeklyMacdLineMinusSignal = snapshotFeatures.WeeklyMACDLineMinusSignal,
                DailyBollingerUpperDistancePct = snapshotFeatures.DailyBollingerUpperDistancePct,
                DailyBollingerBandWidthPct = snapshotFeatures.DailyBollingerBandWidthPct,
                WeeklyBollingerUpperDistancePct = snapshotFeatures.WeeklyBollingerUpperDistancePct,
                WeeklyBollingerBandWidthPct = snapshotFeatures.WeeklyBollingerBandWidthPct,
                Pullback10d = CalculatePullbackByCalendarDays(snapshotCandles, 10),
                DailyPullback10d = CalculateDailyPullback10d(snapshotCandles),
                VolumeRatio20 = CalculateVolumeRatio20(snapshotCandles),
                AtrRatio = CalculateAtrRatio(snapshotCandles, 14),
                TrendPosition = snapshotFeatures.H4MaSignedDistancePct,
                DailyTrendPosition = snapshotFeatures.DailyMaSignedDistancePct,
                BbMidSignedDistancePct = CalculateSmaSignedDistancePct(snapshotCandles, 20),
                WeeklyMacdHistDelta = CalculateWeeklyMacdHistDelta(snapshotCandles)
            };

            FillTrailingSeries(row, candles, latestIndex, featureCache);
            return row;
        }

        private void FillTrailingSeries(
            ResearchDatasetRow row,
            List<Candle> candles,
            int scanIndex,
            Dictionary<int, FeatureSet> featureCache)
        {
            row.DailyMaDistances = BuildRecentDailySeries(candles, scanIndex, featureCache, x => x.DailyMaSignedDistancePct);
            row.DailyBollingerMidDistances = BuildRecentDailySeries(candles, scanIndex, featureCache, x => x.DailyBollingerMidDistancePct);
            row.DailyBollingerUpperDistances = BuildRecentDailySeries(candles, scanIndex, featureCache, x => x.DailyBollingerUpperDistancePct);
            row.DailyBollingerBandWidths = BuildRecentDailySeries(candles, scanIndex, featureCache, x => x.DailyBollingerBandWidthPct);
            row.DailyRsiValues = BuildRecentDailySeries(candles, scanIndex, featureCache, x => x.DailyRSI14);
            row.DailyMacdValues = BuildRecentDailySeries(candles, scanIndex, featureCache, x => x.DailyMACDLineMinusSignal);

            row.WeeklyMaDistances = BuildRecentWeeklySeries(candles, scanIndex, featureCache, x => x.WeeklyMaSignedDistancePct);
            row.WeeklyBollingerMidDistances = BuildRecentWeeklySeries(candles, scanIndex, featureCache, x => x.WeeklyBollingerMidDistancePct);
            row.WeeklyBollingerUpperDistances = BuildRecentWeeklySeries(candles, scanIndex, featureCache, x => x.WeeklyBollingerUpperDistancePct);
            row.WeeklyBollingerBandWidths = BuildRecentWeeklySeries(candles, scanIndex, featureCache, x => x.WeeklyBollingerBandWidthPct);
            row.WeeklyRsiValues = BuildRecentWeeklySeries(candles, scanIndex, featureCache, x => x.WeeklyRSI14);
            row.WeeklyMacdValues = BuildRecentWeeklySeries(candles, scanIndex, featureCache, x => x.WeeklyMACDLineMinusSignal);

            row.H4MaDistances = BuildRecentH4Series(candles, scanIndex, featureCache, x => x.H4MaSignedDistancePct);
            row.H4BollingerMidDistances = BuildRecentH4Series(candles, scanIndex, featureCache, x => x.H4BollingerMidDistancePct);
            row.H4BollingerUpperDistances = BuildRecentH4Series(candles, scanIndex, featureCache, x => x.H4BollingerUpperDistancePct);
            row.H4BollingerBandWidths = BuildRecentH4Series(candles, scanIndex, featureCache, x => x.H4BollingerBandWidthPct);
            row.H4RsiValues = BuildRecentH4Series(candles, scanIndex, featureCache, x => x.RSI14);
            row.H4MacdValues = BuildRecentH4Series(candles, scanIndex, featureCache, x => x.MACDLineMinusSignal);
        }

        private List<decimal> BuildRecentDailySeries(
            List<Candle> candles,
            int scanIndex,
            Dictionary<int, FeatureSet> featureCache,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();
            var usedDays = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var day = candles[i].Time.Date;
                if (!usedDays.Add(day))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentDailySeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => decimal.Round(selector(GetFeatures(candles, i, featureCache)), 2, MidpointRounding.AwayFromZero))];
        }

        private List<decimal> BuildRecentWeeklySeries(
            List<Candle> candles,
            int scanIndex,
            Dictionary<int, FeatureSet> featureCache,
            Func<FeatureSet, decimal?> selector)
        {
            var indexes = new List<int>();
            var usedWeeks = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var week = GetWeekStart(candles[i].Time);
                if (!usedWeeks.Add(week))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentWeeklySeriesLength)
                    break;
            }

            indexes.Reverse();

            return [.. indexes
                .Select(i => selector(GetFeatures(candles, i, featureCache)))
                .Where(x => x.HasValue)
                .Select(x => decimal.Round(x!.Value, 2, MidpointRounding.AwayFromZero))];
        }

        private List<decimal> BuildRecentH4Series(
            List<Candle> candles,
            int scanIndex,
            Dictionary<int, FeatureSet> featureCache,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();

            for (var i = scanIndex; i >= 0; i--)
            {
                indexes.Add(i);
                if (indexes.Count >= RecentH4SeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => decimal.Round(selector(GetFeatures(candles, i, featureCache)), 2, MidpointRounding.AwayFromZero))];
        }

        private FeatureSet GetFeatures(
            List<Candle> candles,
            int candleIndex,
            Dictionary<int, FeatureSet> featureCache)
        {
            if (featureCache.TryGetValue(candleIndex, out var cached))
                return cached;

            var features = _featureEngine.Calculate(candles, candleIndex + 1);
            featureCache[candleIndex] = features;
            return features;
        }

        private static decimal CalculatePullbackByCalendarDays(List<Candle> candles, int days)
        {
            if (candles.Count == 0)
                return 0m;

            var lastTime = candles[^1].Time;
            var start = lastTime.AddDays(-days);

            var range = candles
                .Where(x => x.Time >= start)
                .ToList();

            if (range.Count == 0)
                return 0m;

            var highest = range.Max(x => x.High);
            var close = candles[^1].Close;

            if (highest <= 0m)
                return 0m;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalculateVolumeRatio20(List<Candle> candles)
        {
            if (candles.Count < 21)
                return 0m;

            var currentVolume = candles[^1].Volume;
            var avgVolume20 = candles
                .Skip(candles.Count - 21)
                .Take(20)
                .Average(x => x.Volume);

            if (avgVolume20 <= 0m)
                return 0m;

            return currentVolume / avgVolume20;
        }

        private static decimal CalculateAtrRatio(List<Candle> candles, int length)
        {
            if (candles.Count < length + 1)
                return 0m;

            var trueRanges = new List<decimal>();

            for (var i = candles.Count - length; i < candles.Count; i++)
            {
                var current = candles[i];
                var prevClose = candles[i - 1].Close;

                var tr = Math.Max(
                    current.High - current.Low,
                    Math.Max(
                        Math.Abs(current.High - prevClose),
                        Math.Abs(current.Low - prevClose)));

                trueRanges.Add(tr);
            }

            if (trueRanges.Count == 0)
                return 0m;

            var atr = trueRanges.Average();
            var close = candles[^1].Close;

            if (close == 0m)
                return 0m;

            return atr / close * 100m;
        }

        private static decimal CalculateDailyPullback10d(List<Candle> candles)
        {
            var dailyBars = BuildDailyBars(candles);

            if (dailyBars.Count == 0)
                return 0m;

            var range = dailyBars.TakeLast(10).ToList();
            var highest = range.Max(x => x.High);
            var close = dailyBars[^1].Close;

            if (highest <= 0m)
                return 0m;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalculateSmaSignedDistancePct(List<Candle> candles, int length)
        {
            if (candles.Count < length)
                return 0m;

            var sma = candles.TakeLast(length).Average(x => x.Close);

            if (sma == 0m)
                return 0m;

            var close = candles[^1].Close;
            return (close - sma) / sma * 100m;
        }

        private static decimal? CalculateWeeklyMacdHistDelta(List<Candle> candles)
        {
            var weeklyCloses = BuildWeeklyBars(candles)
                .Select(x => x.Close)
                .ToList();

            var macdSeries = BuildMacdSeries(weeklyCloses);

            if (macdSeries.Count < 2)
                return null;

            var currentHist = macdSeries[^1].Macd - macdSeries[^1].Signal;
            var prevHist = macdSeries[^2].Macd - macdSeries[^2].Signal;

            return currentHist - prevHist;
        }

        private static List<Candle> BuildDailyBars(List<Candle> candles)
        {
            var result = new List<Candle>();

            foreach (var group in candles.GroupBy(x => x.Time.Date).OrderBy(x => x.Key))
            {
                var ordered = group.OrderBy(x => x.Time).ToList();

                result.Add(new Candle
                {
                    Timeframe = Timeframe.D1,
                    Time = group.Key,
                    Open = ordered[0].Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = ordered[^1].Close,
                    Volume = ordered.Sum(x => x.Volume)
                });
            }

            return result;
        }

        private static List<Candle> BuildWeeklyBars(List<Candle> candles)
        {
            var result = new List<Candle>();

            foreach (var group in candles
                .GroupBy(x => GetWeekStart(x.Time.Date))
                .OrderBy(x => x.Key))
            {
                var ordered = group.OrderBy(x => x.Time).ToList();

                result.Add(new Candle
                {
                    Timeframe = Timeframe.W1,
                    Time = group.Key,
                    Open = ordered[0].Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = ordered[^1].Close,
                    Volume = ordered.Sum(x => x.Volume)
                });
            }

            return result;
        }

        private static List<MacdPoint> BuildMacdSeries(List<decimal> closes)
        {
            var result = new List<MacdPoint>();

            if (closes.Count == 0)
                return result;

            decimal? ema12 = null;
            decimal? ema26 = null;
            decimal? signal = null;

            const decimal k12 = 2m / 13m;
            const decimal k26 = 2m / 27m;
            const decimal k9 = 2m / 10m;

            foreach (var close in closes)
            {
                ema12 = ema12 == null ? close : ema12.Value + (close - ema12.Value) * k12;
                ema26 = ema26 == null ? close : ema26.Value + (close - ema26.Value) * k26;

                var macd = ema12.Value - ema26.Value;
                signal = signal == null ? macd : signal.Value + (macd - signal.Value) * k9;

                result.Add(new MacdPoint
                {
                    Macd = macd,
                    Signal = signal.Value
                });
            }

            return result;
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

        private sealed class MacdPoint
        {
            public decimal Macd { get; set; }
            public decimal Signal { get; set; }
        }
    }
}
