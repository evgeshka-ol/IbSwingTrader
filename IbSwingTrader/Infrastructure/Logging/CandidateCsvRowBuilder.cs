using System.Collections;
using System.Globalization;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateCsvRowBuilder(
        INumberTextFormatter numberFormatter,
        IArrayCellFormatter arrayCellFormatter,
        IObjectPropertyReader objectPropertyReader) : ICandidateCsvRowBuilder
    {
        private readonly INumberTextFormatter _fmt = numberFormatter;
        private readonly IArrayCellFormatter _arrayFmt = arrayCellFormatter;
        private readonly IObjectPropertyReader _propertyReader = objectPropertyReader;

        public CandidateCsvTable Build(
            IEnumerable<CandidateDetails> candidates,
            IEnumerable<CandidateDetails> sameDayCandidates,
            IReadOnlySet<string> currentOperationKeys)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            ArgumentNullException.ThrowIfNull(sameDayCandidates);
            ArgumentNullException.ThrowIfNull(currentOperationKeys);

            var headers = new List<string>();
            var rows = new List<Dictionary<string, string>>();

            AddGroupRows("Runaway", sameDayCandidates, currentOperationKeys, headers, rows);
            AddGroupRows("Reversal", candidates, currentOperationKeys, headers, rows);

            return new CandidateCsvTable
            {
                Headers = headers,
                Rows = rows
                    .OrderByDescending(x => ParseDateTime(x.GetValueOrDefault("ScanTime")))
                    .ThenBy(x => GetCandidateGroupOrder(x.GetValueOrDefault("CandidateGroup")))
                    .ThenBy(x => ParseInt(x.GetValueOrDefault("DisplayRank")) ?? int.MaxValue)
                    .ThenBy(x => x.GetValueOrDefault("Ticker"), StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private static int GetCandidateGroupOrder(string? candidateGroup)
        {
            return candidateGroup?.Equals("Runaway", StringComparison.OrdinalIgnoreCase) == true
                ? 0
                : 1;
        }

        private void AddGroupRows(
            string groupName,
            IEnumerable<CandidateDetails> candidates,
            IReadOnlySet<string> currentOperationKeys,
            List<string> headers,
            List<Dictionary<string, string>> rows)
        {
            foreach (var scanGroup in candidates
                         .GroupBy(x => x.Scan.ScanTime)
                         .OrderByDescending(x => x.Key))
            {
                var rank = 1;
                foreach (var candidate in OrderForDisplay(scanGroup))
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    Add(row, headers, "Ticker", candidate.Ticker);
                    Add(row, headers, nameof(candidate.Scan.ScanTime), FormatValue(candidate.Scan.ScanTime));
                    Add(row, headers, "CandidateGroup", groupName);
                    FlattenObject(row, headers, "TradePlan", candidate.TradePlan);
                    Add(row, headers, "DisplayRank", rank.ToString(CultureInfo.InvariantCulture));
                    Add(row, headers, "IsCurrentScanOutput", currentOperationKeys.Contains(BuildCandidateOperationKey(candidate)) ? "true" : "false");
                    FlattenObject(row, headers, string.Empty, candidate.Scan);

                    Add(row, headers, nameof(candidate.CandidateSource), candidate.CandidateSource);
                    Add(row, headers, nameof(candidate.IsFromWishlist), FormatValue(candidate.IsFromWishlist));
                    Add(row, headers, nameof(candidate.NeedsDeeperEntry), FormatValue(candidate.NeedsDeeperEntry));
                    Add(row, headers, nameof(candidate.NeedsMomentumExit), FormatValue(candidate.NeedsMomentumExit));

                    FlattenSeriesAndRegimeFields(row, headers, candidate);
                    FlattenObject(row, headers, "Score", candidate.Score);
                    FlattenObject(row, headers, "Context", candidate.Context);

                    if (candidate.Diagnostics != null)
                        FlattenObject(row, headers, "Diagnostics", candidate.Diagnostics);

                    rows.Add(row);
                    rank++;
                }
            }
        }

        private void FlattenSeriesAndRegimeFields(
            Dictionary<string, string> row,
            List<string> headers,
            CandidateDetails candidate)
        {
            Add(row, headers, nameof(candidate.RecentDailyBbUpperBandSeries), FormatValue(candidate.RecentDailyBbUpperBandSeries));
            Add(row, headers, nameof(candidate.RecentDailyBbMidBandSeries), FormatValue(candidate.RecentDailyBbMidBandSeries));
            Add(row, headers, nameof(candidate.RecentDailyBbLowerBandSeries), FormatValue(candidate.RecentDailyBbLowerBandSeries));
            Add(row, headers, nameof(candidate.RecentDailyMacdLineSeries), FormatValue(candidate.RecentDailyMacdLineSeries));
            Add(row, headers, nameof(candidate.RecentDailyMacdSignalSeries), FormatValue(candidate.RecentDailyMacdSignalSeries));
            Add(row, headers, nameof(candidate.RecentDailyMacdHistogramSeries), FormatValue(candidate.RecentDailyMacdHistogramSeries));
            Add(row, headers, nameof(candidate.RecentDailyRsiSeries), FormatValue(candidate.RecentDailyRsiSeries));

            Add(row, headers, nameof(candidate.RecentWeeklyBbUpperBandSeries), FormatValue(candidate.RecentWeeklyBbUpperBandSeries));
            Add(row, headers, nameof(candidate.RecentWeeklyBbMidBandSeries), FormatValue(candidate.RecentWeeklyBbMidBandSeries));
            Add(row, headers, nameof(candidate.RecentWeeklyBbLowerBandSeries), FormatValue(candidate.RecentWeeklyBbLowerBandSeries));
            Add(row, headers, nameof(candidate.RecentWeeklyMacdLineSeries), FormatValue(candidate.RecentWeeklyMacdLineSeries));
            Add(row, headers, nameof(candidate.RecentWeeklyMacdSignalSeries), FormatValue(candidate.RecentWeeklyMacdSignalSeries));
            Add(row, headers, nameof(candidate.RecentWeeklyMacdHistogramSeries), FormatValue(candidate.RecentWeeklyMacdHistogramSeries));
            Add(row, headers, nameof(candidate.RecentWeeklyRsiSeries), FormatValue(candidate.RecentWeeklyRsiSeries));

            Add(row, headers, nameof(candidate.RecentH4BbUpperBandSeries), FormatValue(candidate.RecentH4BbUpperBandSeries));
            Add(row, headers, nameof(candidate.RecentH4BbMidBandSeries), FormatValue(candidate.RecentH4BbMidBandSeries));
            Add(row, headers, nameof(candidate.RecentH4BbLowerBandSeries), FormatValue(candidate.RecentH4BbLowerBandSeries));
            Add(row, headers, nameof(candidate.RecentH4MacdLineSeries), FormatValue(candidate.RecentH4MacdLineSeries));
            Add(row, headers, nameof(candidate.RecentH4MacdSignalSeries), FormatValue(candidate.RecentH4MacdSignalSeries));
            Add(row, headers, nameof(candidate.RecentH4MacdHistogramSeries), FormatValue(candidate.RecentH4MacdHistogramSeries));
            Add(row, headers, nameof(candidate.RecentH4RsiSeries), FormatValue(candidate.RecentH4RsiSeries));
        }

        private void FlattenObject(
            Dictionary<string, string> row,
            List<string> headers,
            string prefix,
            object value)
        {
            foreach (var property in _propertyReader.GetOrderedProperties(value.GetType()))
            {
                if (ShouldSkipCsvProperty(prefix, property.Name))
                    continue;

                var name = string.IsNullOrWhiteSpace(prefix)
                    ? property.Name
                    : $"{prefix}{property.Name}";

                Add(row, headers, name, FormatValue(property.GetValue(value)));
            }
        }

        private static bool ShouldSkipCsvProperty(string prefix, string propertyName)
        {
            if (!prefix.Equals("Diagnostics", StringComparison.OrdinalIgnoreCase))
                return false;

            return propertyName is
                nameof(CandidateDiagnostics.SeriesSimilarityTemplateTicker) or
                nameof(CandidateDiagnostics.SeriesSimilarityTemplateFamily) or
                nameof(CandidateDiagnostics.SeriesSimilarityBonus) or
                nameof(CandidateDiagnostics.LowAmplitudeTemplateTicker) or
                nameof(CandidateDiagnostics.LowAmplitudePenalty);
        }

        private static List<CandidateDetails> OrderForDisplay(IEnumerable<CandidateDetails> candidates)
        {
            return candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void Add(Dictionary<string, string> row, List<string> headers, string name, string value)
        {
            if (!headers.Contains(name, StringComparer.OrdinalIgnoreCase))
                headers.Add(name);

            row[name] = value;
        }

        private string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            if (value is string s)
                return s;

            if (value is decimal d)
                return _fmt.Generic(d);

            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (value is bool b)
                return b ? "true" : "false";

            if (value is IEnumerable<decimal> decimalValues)
                return _arrayFmt.Format(decimalValues);

            if (value is IEnumerable enumerable && value is not string)
            {
                var decimals = new List<decimal>();

                foreach (var item in enumerable)
                {
                    if (item is decimal decimalItem)
                        decimals.Add(decimalItem);
                }

                return _arrayFmt.Format(decimals);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static DateTime ParseDateTime(string? value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
                ? result
                : DateTime.MinValue;
        }

        private static int? ParseInt(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : null;
        }

        public static string BuildCandidateOperationKey(CandidateDetails candidate)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.Scan.PresetScanCode}|{candidate.Scan.ScanTime:O}|{candidate.TradePlan.EntryPrice:G29}|{candidate.TradePlan.ExitPrice:G29}|{candidate.TradePlan.StopLoss:G29}");
        }
    }
}
