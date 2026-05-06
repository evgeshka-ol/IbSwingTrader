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
                $"Research settings: Mode={settings.Mode}, Source={settings.Source}, LookbackCalendarDays={settings.LookbackCalendarDays}, MinimumCandles={settings.MinimumCandles}, MinRunupPct={settings.MinRunupPct}, MaxBarsToPeak={settings.MaxBarsToPeak}, EpisodeMergeCooldownBars={settings.EpisodeMergeCooldownBars}");
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
                    var delimiter = DetectDelimiter(lines[0]);
                    var headers = SplitCsvLine(lines[0], delimiter);
                    var tickerIndex = headers.FindIndex(x => string.Equals(x, "Ticker", StringComparison.OrdinalIgnoreCase));

                    if (tickerIndex >= 0)
                    {
                        foreach (var line in lines.Skip(1))
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            var parts = SplitCsvLine(line, delimiter);
                            if (parts.Count > tickerIndex && !string.IsNullOrWhiteSpace(parts[tickerIndex]))
                                result.Add(parts[tickerIndex].Trim());
                        }
                    }
                }
            }

            return [.. result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
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

            var candidates = BuildEpisodeCandidates(candles, settings);
            var merged = MergeEpisodeCandidates(candidates, settings.EpisodeMergeCooldownBars);

            return [.. merged.Select(x => BuildRow(ticker, settings, candles, x))];
        }

        private static List<ResearchEpisodeCandidate> BuildEpisodeCandidates(
            List<Candle> candles,
            ResearchSettings settings)
        {
            var episodes = new List<ResearchEpisodeCandidate>();

            for (var i = settings.LocalExtremaLookbackBars; i < candles.Count - settings.LocalExtremaLookbackBars - 1; i++)
            {
                if (!IsLocalMinimum(candles, i, settings.LocalExtremaLookbackBars))
                    continue;

                var maxForwardIndex = Math.Min(candles.Count - 1, i + settings.MaxBarsToPeak);
                var peakIndex = i;
                var peakHigh = candles[i].High;
                var minLowAfterReference = candles[i].Low;

                for (var j = i + 1; j <= maxForwardIndex; j++)
                {
                    if (candles[j].High > peakHigh)
                    {
                        peakHigh = candles[j].High;
                        peakIndex = j;
                    }

                    if (candles[j].Low < minLowAfterReference)
                        minLowAfterReference = candles[j].Low;
                }

                var referencePrice = candles[i].Close;
                if (referencePrice <= 0m || peakIndex <= i)
                    continue;

                var runupPct = (peakHigh - referencePrice) / referencePrice * 100m;
                if (runupPct < settings.MinRunupPct)
                    continue;

                var maxDrawdownPct = (minLowAfterReference - referencePrice) / referencePrice * 100m;

                episodes.Add(new ResearchEpisodeCandidate
                {
                    ReferenceIndex = i,
                    PeakIndex = peakIndex,
                    PeakHigh = peakHigh,
                    MinLowAfterReference = minLowAfterReference,
                    RunupPct = runupPct,
                    MaxDrawdownPct = maxDrawdownPct
                });
            }

            return episodes;
        }

        private static List<ResearchEpisodeCandidate> MergeEpisodeCandidates(
            List<ResearchEpisodeCandidate> episodes,
            int cooldownBars)
        {
            if (episodes.Count == 0)
                return [];

            var ordered = episodes
                .OrderBy(x => x.ReferenceIndex)
                .ThenByDescending(x => x.RunupPct)
                .ToList();

            var result = new List<ResearchEpisodeCandidate>();
            var clusterBest = ordered[0];
            var clusterEnd = ordered[0].PeakIndex;

            for (var i = 1; i < ordered.Count; i++)
            {
                var next = ordered[i];

                if (next.ReferenceIndex <= clusterEnd + cooldownBars)
                {
                    clusterEnd = Math.Max(clusterEnd, next.PeakIndex);

                    if (IsBetterEpisode(next, clusterBest))
                        clusterBest = next;

                    continue;
                }

                result.Add(clusterBest);
                clusterBest = next;
                clusterEnd = next.PeakIndex;
            }

            result.Add(clusterBest);
            return result;
        }

        private static bool IsBetterEpisode(ResearchEpisodeCandidate candidate, ResearchEpisodeCandidate currentBest)
        {
            if (candidate.RunupPct != currentBest.RunupPct)
                return candidate.RunupPct > currentBest.RunupPct;

            if (candidate.MaxDrawdownPct != currentBest.MaxDrawdownPct)
                return candidate.MaxDrawdownPct > currentBest.MaxDrawdownPct;

            return candidate.ReferenceIndex < currentBest.ReferenceIndex;
        }

        private ResearchDatasetRow BuildRow(
            string ticker,
            ResearchSettings settings,
            List<Candle> candles,
            ResearchEpisodeCandidate episode)
        {
            var referenceFeatures = _featureEngine.Calculate(candles, episode.ReferenceIndex + 1);
            var referencePrice = candles[episode.ReferenceIndex].Close;
            var referenceCandles = candles.Take(episode.ReferenceIndex + 1).ToList();

            var row = new ResearchDatasetRow
            {
                Ticker = ticker,
                Mode = settings.Mode,
                Source = settings.Source,
                ReferenceType = "OracleBottom",
                ReferenceTime = candles[episode.ReferenceIndex].Time,
                ReferencePrice = referencePrice,
                PeakTime = candles[episode.PeakIndex].Time,
                PeakPrice = episode.PeakHigh,
                RunupPct = episode.RunupPct,
                BarsToPeak = episode.PeakIndex - episode.ReferenceIndex,
                MaxDrawdownBeforePeakPct = episode.MaxDrawdownPct,
                DistanceTo20dHigh = referenceFeatures.DistanceTo20dHigh,
                DistanceTo52wHigh = referenceFeatures.DistanceTo52wHigh,
                H4MaSignedDistancePct = referenceFeatures.H4MaSignedDistancePct,
                H4Rsi14 = referenceFeatures.RSI14,
                H4MacdLineMinusSignal = referenceFeatures.MACDLineMinusSignal,
                DailyMaSignedDistancePct = referenceFeatures.DailyMaSignedDistancePct,
                DailyRsi14 = referenceFeatures.DailyRSI14,
                DailyMacdLineMinusSignal = referenceFeatures.DailyMACDLineMinusSignal,
                WeeklyMaSignedDistancePct = referenceFeatures.WeeklyMaSignedDistancePct,
                WeeklyRsi14 = referenceFeatures.WeeklyRSI14,
                WeeklyMacdLineMinusSignal = referenceFeatures.WeeklyMACDLineMinusSignal,
                DailyBollingerUpperDistancePct = referenceFeatures.DailyBollingerUpperDistancePct,
                DailyBollingerBandWidthPct = referenceFeatures.DailyBollingerBandWidthPct,
                WeeklyBollingerUpperDistancePct = referenceFeatures.WeeklyBollingerUpperDistancePct,
                WeeklyBollingerBandWidthPct = referenceFeatures.WeeklyBollingerBandWidthPct,
                Pullback10d = CalculatePullbackByCalendarDays(referenceCandles, 10),
                DailyPullback10d = CalculateDailyPullback10d(referenceCandles),
                VolumeRatio20 = CalculateVolumeRatio20(referenceCandles),
                AtrRatio = CalculateAtrRatio(referenceCandles, 14),
                TrendPosition = referenceFeatures.H4MaSignedDistancePct,
                DailyTrendPosition = referenceFeatures.DailyMaSignedDistancePct,
                BbMidSignedDistancePct = CalculateSmaSignedDistancePct(referenceCandles, 20),
                WeeklyMacdHistDelta = CalculateWeeklyMacdHistDelta(referenceCandles)
            };

            FillSeries(row, candles, episode.ReferenceIndex, episode.PeakIndex);
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
                row.DailyBollingerMidDistances.Add(features.DailyBollingerMidDistancePct);
                row.DailyBollingerUpperDistances.Add(features.DailyBollingerUpperDistancePct);
                row.DailyBollingerBandWidths.Add(features.DailyBollingerBandWidthPct);
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
                if (features.WeeklyBollingerMidDistancePct.HasValue)
                    row.WeeklyBollingerMidDistances.Add(features.WeeklyBollingerMidDistancePct.Value);
                if (features.WeeklyBollingerUpperDistancePct.HasValue)
                    row.WeeklyBollingerUpperDistances.Add(features.WeeklyBollingerUpperDistancePct.Value);
                if (features.WeeklyBollingerBandWidthPct.HasValue)
                    row.WeeklyBollingerBandWidths.Add(features.WeeklyBollingerBandWidthPct.Value);

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
            row.H4BollingerMidDistances = [];
            row.H4BollingerUpperDistances = [];
            row.H4BollingerBandWidths = [];
            row.H4RsiValues = [];
            row.H4MacdValues = [];

            for (var i = entryIndex; i <= exitIndex; i++)
            {
                var features = _featureEngine.Calculate(candles, i + 1);
                row.H4MaDistances.Add(features.H4MaSignedDistancePct);
                row.H4BollingerMidDistances.Add(features.H4BollingerMidDistancePct);
                row.H4BollingerUpperDistances.Add(features.H4BollingerUpperDistancePct);
                row.H4BollingerBandWidths.Add(features.H4BollingerBandWidthPct);
                row.H4RsiValues.Add(features.RSI14);
                row.H4MacdValues.Add(features.MACDLineMinusSignal);
            }
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

        private sealed class ResearchEpisodeCandidate
        {
            public int ReferenceIndex { get; set; }
            public int PeakIndex { get; set; }
            public decimal PeakHigh { get; set; }
            public decimal MinLowAfterReference { get; set; }
            public decimal RunupPct { get; set; }
            public decimal MaxDrawdownPct { get; set; }
        }

        private sealed class MacdPoint
        {
            public decimal Macd { get; set; }
            public decimal Signal { get; set; }
        }
    }
}
