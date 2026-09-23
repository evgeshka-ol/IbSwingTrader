using IbSwingTrader.Abstractions.Evaluation;
using IbSwingTrader.Application.Candidates;
using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Domain.Dataset;

namespace IbSwingTrader.Application.Evaluation
{
    public sealed class CandidatePatternVerdictService(
        IBollingerFigureAnalyzer bollingerFigureAnalyzer) : ICandidatePatternVerdictService
    {
        private readonly IBollingerFigureAnalyzer _bollingerFigureAnalyzer = bollingerFigureAnalyzer;

        public CandidatePatternVerdict Analyze(CandidateEvaluationResult candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            return AnalyzeInternal(new PatternSeries(candidate));
        }

        public CandidatePatternVerdict Analyze(EvaluationDatasetRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            return AnalyzeInternal(new PatternSeries(row));
        }

        private CandidatePatternVerdict AnalyzeInternal(PatternSeries series)
        {
            var dailyState = AnalyzeTimeframe(
                series.DailyBbMidBandSeries,
                series.DailyBbUpperBandSeries,
                series.DailyBbLowerBandSeries);
            var h4State = AnalyzeTimeframe(
                series.H4BbMidBandSeries,
                series.H4BbUpperBandSeries,
                series.H4BbLowerBandSeries);
            var bellSignal = ClassifyBellPatternSignal(
                series.DailyBbUpperBandSeries,
                series.DailyBbMidBandSeries,
                series.DailyBbLowerBandSeries,
                series.H4BbUpperBandSeries,
                series.H4BbMidBandSeries,
                series.H4BbLowerBandSeries,
                series.DailyRsiSeries,
                series.DailyMacdHistogramSeries,
                series.H4RsiSeries,
                series.H4MacdHistogramSeries,
                dailyState.Direction,
                h4State.Direction);

            // Pattern precedence is independent of the legacy daily family split.
            if (IsRunawayBellUpPattern(bellSignal, dailyState, h4State) &&
                !IsH4ContradictingDailyBellUp(series, bellSignal, h4State))
            {
                if (BellPatternClassifier.IsVerticalSpikeExpansion(
                        TimeframeSeries(series, bellSignal.Timeframe, isUpper: true),
                        TimeframeSeries(series, bellSignal.Timeframe, isUpper: false),
                        bellSignal.Timeframe == BellPatternTimeframe.H4 ? series.H4RsiSeries : series.DailyRsiSeries,
                        bellSignal.Timeframe == BellPatternTimeframe.H4 ? series.H4MacdHistogramSeries : series.DailyMacdHistogramSeries))
                {
                    return new CandidatePatternVerdict(
                        "BellUp",
                        "BellUp",
                        "Mismatch",
                        $"BellUp not confirmed on {bellSignal.Timeframe}: isolated terminal spike");
                }

                if (HasTerminalMomentumRollover(series, bellSignal.Timeframe))
                    return new CandidatePatternVerdict("BellUp", "None", "Mismatch", $"BellUp not confirmed on {bellSignal.Timeframe}: expansion ended in momentum rollover");

                return new CandidatePatternVerdict("BellUp", "BellUp", "Match", $"BellUp confirmed on {bellSignal.Timeframe}");
            }

            if (BellPatternClassifier.IsReversalHookPattern(
                    series.DailyCloseSeries,
                    series.DailyBbUpperBandSeries,
                    series.DailyBbMidBandSeries,
                    series.DailyBbLowerBandSeries,
                    series.DailyRsiSeries,
                    series.DailyMacdLineSeries,
                    series.DailyMacdSignalSeries,
                    series.DailyMacdHistogramSeries,
                    out var diagnostics))
            {
                return new CandidatePatternVerdict(
                    "ReversalHook",
                    "ReversalHook",
                    "Match",
                    "ReversalHook confirmed");
            }

            return new CandidatePatternVerdict(
                "Other",
                "None",
                "Mismatch",
                diagnostics);
        }

        private static List<decimal> TimeframeSeries(PatternSeries series, BellPatternTimeframe timeframe, bool isUpper)
        {
            if (timeframe == BellPatternTimeframe.H4)
                return isUpper ? series.H4BbUpperBandSeries : series.H4BbLowerBandSeries;

            return isUpper ? series.DailyBbUpperBandSeries : series.DailyBbLowerBandSeries;
        }

        private BollingerFigureState AnalyzeTimeframe(
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> lower)
        {
            var count = Math.Min(mid.Count, Math.Min(upper.Count, lower.Count));
            var alignedMid = mid.TakeLast(count).ToList();
            var alignedUpper = upper.TakeLast(count).ToList();
            var alignedLower = lower.TakeLast(count).ToList();

            return _bollingerFigureAnalyzer.Analyze(new BollingerFeatureSeries
            {
                UpperSeries = alignedUpper,
                MidSeries = alignedMid,
                LowerSeries = alignedLower
            });
        }

        private static BellPatternSignal ClassifyBellPatternSignal(
            IReadOnlyList<decimal> dailyUpper,
            IReadOnlyList<decimal> dailyMid,
            IReadOnlyList<decimal> dailyLower,
            IReadOnlyList<decimal> h4Upper,
            IReadOnlyList<decimal> h4Mid,
            IReadOnlyList<decimal> h4Lower,
            IReadOnlyList<decimal> dailyRsi,
            IReadOnlyList<decimal> dailyMacdHistogram,
            IReadOnlyList<decimal> h4Rsi,
            IReadOnlyList<decimal> h4MacdHistogram,
            BollingerFigureDirection dailyDirection,
            BollingerFigureDirection h4Direction)
        {
            var dailyKind = BellPatternClassifier.ClassifyBellPatternKindForTimeframe(dailyUpper, dailyMid, dailyLower, dailyDirection, BellPatternTimeframe.Daily);
            var h4Kind = BellPatternClassifier.ClassifyBellPatternKindForTimeframe(h4Upper, h4Mid, h4Lower, h4Direction, BellPatternTimeframe.H4);

            if (dailyKind == BellPatternKind.BellUp &&
                h4Kind == BellPatternKind.BellUp &&
                BellPatternClassifier.IsVerticalSpikeExpansion(h4Upper, h4Lower, h4Rsi, h4MacdHistogram) &&
                !BellPatternClassifier.IsVerticalSpikeExpansion(dailyUpper, dailyLower, dailyRsi, dailyMacdHistogram))
            {
                return new BellPatternSignal(BellPatternKind.BellUp, BellPatternTimeframe.Daily);
            }

            var bellUpSignal = BellPatternClassifier.SelectBellPatternSignal(dailyKind, h4Kind, BellPatternKind.BellUp);
            if (bellUpSignal.Kind != BellPatternKind.None)
                return bellUpSignal;

            return BellPatternClassifier.SelectBellPatternSignal(dailyKind, h4Kind, BellPatternKind.BellDown);
        }

        private static bool IsRunawayBellUpPattern(BellPatternSignal bellPatternSignal, BollingerFigureState dailyState, BollingerFigureState h4State)
        {
            if (bellPatternSignal.Kind != BellPatternKind.BellUp)
                return false;

            return bellPatternSignal.Timeframe switch
            {
                BellPatternTimeframe.H4 => h4State.Direction != BollingerFigureDirection.Down &&
                                           h4State.Regime != BollingerFigureRegime.Collapse,
                BellPatternTimeframe.Daily => dailyState.Direction != BollingerFigureDirection.Down &&
                                              dailyState.Regime != BollingerFigureRegime.Collapse,
                _ => false
            };
        }

        private static bool IsH4ContradictingDailyBellUp(
            PatternSeries series,
            BellPatternSignal bellPatternSignal,
            BollingerFigureState h4State)
        {
            if (bellPatternSignal.Timeframe != BellPatternTimeframe.Daily)
                return false;

            if (h4State.Direction == BollingerFigureDirection.Down ||
                h4State.Regime == BollingerFigureRegime.Collapse)
                return true;

            if (BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
                    series.H4BbUpperBandSeries,
                    series.H4BbMidBandSeries,
                    series.H4BbLowerBandSeries,
                    h4State.Direction,
                    BellPatternTimeframe.H4) == BellPatternKind.BellUp)
            {
                return false;
            }

            var h4RsiTail = BellPatternClassifier.CalculateTailSlope(series.H4RsiSeries, 4);
            var h4MacdHistogramTail = BellPatternClassifier.CalculateTailSlope(series.H4MacdHistogramSeries, 4);

            return h4RsiTail < -3m &&
                   h4MacdHistogramTail <= 0m;
        }

        private static bool IsH4ContradictingDailyReversalHook(
            PatternSeries series,
            BollingerFigureState h4State,
            out string diagnostics)
        {
            var h4RsiTail = BellPatternClassifier.CalculateTailSlope(series.H4RsiSeries, 4);
            var h4MacdHistogramTail = BellPatternClassifier.CalculateTailSlope(series.H4MacdHistogramSeries, 4);

            diagnostics =
                $"H4 does not confirm daily ReversalHook, " +
                $"H4={h4State.Regime}/{h4State.Direction}, " +
                $"H4RsiTail={h4RsiTail}, " +
                $"H4MacdHistogramTail={h4MacdHistogramTail}";

            if (h4State.Regime == BollingerFigureRegime.Collapse)
                return true;

            if (h4State.Direction == BollingerFigureDirection.Down &&
                h4RsiTail <= 0m)
                return true;

            return false;
        }

        private static bool HasTerminalMomentumRollover(
            PatternSeries series,
            BellPatternTimeframe timeframe)
        {
            IReadOnlyList<decimal> macdLine = timeframe switch
            {
                BellPatternTimeframe.H4 => series.H4MacdLineSeries,
                BellPatternTimeframe.Daily => series.DailyMacdLineSeries,
                _ => []
            };
            IReadOnlyList<decimal> macdHistogram = timeframe switch
            {
                BellPatternTimeframe.H4 => series.H4MacdHistogramSeries,
                BellPatternTimeframe.Daily => series.DailyMacdHistogramSeries,
                _ => []
            };

            if (macdLine.Count < 2 || macdHistogram.Count < 4)
                return false;

            var histogramTail = macdHistogram.TakeLast(4).ToArray();
            var histogramDeclinesContinuously =
                histogramTail[1] < histogramTail[0] &&
                histogramTail[2] < histogramTail[1] &&
                histogramTail[3] < histogramTail[2];
            var macdLineStoppedRising = macdLine[^1] <= macdLine[^2];

            return histogramDeclinesContinuously && macdLineStoppedRising;
        }

        private sealed class PatternSeries
        {
            public PatternSeries(CandidateEvaluationResult candidate)
            {
                CandidateGroup = candidate.CandidateSource.Equals("BellUp", StringComparison.OrdinalIgnoreCase)
                    ? "BellUp"
                    : candidate.CandidateSource.Equals("ReversalHook", StringComparison.OrdinalIgnoreCase)
                        ? "ReversalHook"
                    : candidate.CandidateSource.Equals("Other", StringComparison.OrdinalIgnoreCase) ||
                      candidate.CandidateSource.Equals("DiagnosticRejected", StringComparison.OrdinalIgnoreCase)
                        ? "Other"
                        : candidate.CandidateSource;
                DailyCloseSeries = candidate.RecentDailyCloseSeries;
                DailyBbUpperBandSeries = candidate.RecentDailyBbUpperBandSeries;
                DailyBbMidBandSeries = candidate.RecentDailyBbMidBandSeries;
                DailyBbLowerBandSeries = candidate.RecentDailyBbLowerBandSeries;
                DailyRsiSeries = candidate.RecentDailyRsiSeries;
                DailyMacdLineSeries = candidate.RecentDailyMacdLineSeries;
                DailyMacdSignalSeries = candidate.RecentDailyMacdSignalSeries;
                DailyMacdHistogramSeries = candidate.RecentDailyMacdHistogramSeries;
                WeeklyBbUpperBandSeries = candidate.RecentWeeklyBbUpperBandSeries;
                WeeklyBbMidBandSeries = candidate.RecentWeeklyBbMidBandSeries;
                WeeklyBbLowerBandSeries = candidate.RecentWeeklyBbLowerBandSeries;
                H4BbUpperBandSeries = candidate.RecentH4BbUpperBandSeries;
                H4BbMidBandSeries = candidate.RecentH4BbMidBandSeries;
                H4BbLowerBandSeries = candidate.RecentH4BbLowerBandSeries;
                H4RsiSeries = candidate.RecentH4RsiSeries;
                H4MacdLineSeries = candidate.RecentH4MacdLineSeries;
                H4MacdHistogramSeries = candidate.RecentH4MacdHistogramSeries;
            }

            public PatternSeries(EvaluationDatasetRow row)
            {
                CandidateGroup = row.CandidateGroup;
                DailyCloseSeries = row.RecentDailyCloseSeries;
                DailyBbUpperBandSeries = row.RecentDailyBbUpperBandSeries;
                DailyBbMidBandSeries = row.RecentDailyBbMidBandSeries;
                DailyBbLowerBandSeries = row.RecentDailyBbLowerBandSeries;
                DailyRsiSeries = row.RecentDailyRsiSeries;
                DailyMacdLineSeries = row.RecentDailyMacdLineSeries;
                DailyMacdSignalSeries = row.RecentDailyMacdSignalSeries;
                DailyMacdHistogramSeries = row.RecentDailyMacdHistogramSeries;
                WeeklyBbUpperBandSeries = row.RecentWeeklyBbUpperBandSeries;
                WeeklyBbMidBandSeries = row.RecentWeeklyBbMidBandSeries;
                WeeklyBbLowerBandSeries = row.RecentWeeklyBbLowerBandSeries;
                H4BbUpperBandSeries = row.RecentH4BbUpperBandSeries;
                H4BbMidBandSeries = row.RecentH4BbMidBandSeries;
                H4BbLowerBandSeries = row.RecentH4BbLowerBandSeries;
                H4RsiSeries = row.RecentH4RsiSeries;
                H4MacdLineSeries = row.RecentH4MacdLineSeries;
                H4MacdHistogramSeries = row.RecentH4MacdHistogramSeries;
            }

            public string CandidateGroup { get; }
            public List<decimal> DailyCloseSeries { get; }
            public List<decimal> DailyBbUpperBandSeries { get; }
            public List<decimal> DailyBbMidBandSeries { get; }
            public List<decimal> DailyBbLowerBandSeries { get; }
            public List<decimal> DailyRsiSeries { get; }
            public List<decimal> DailyMacdLineSeries { get; }
            public List<decimal> DailyMacdSignalSeries { get; }
            public List<decimal> DailyMacdHistogramSeries { get; }
            public List<decimal> WeeklyBbUpperBandSeries { get; }
            public List<decimal> WeeklyBbMidBandSeries { get; }
            public List<decimal> WeeklyBbLowerBandSeries { get; }
            public List<decimal> H4BbUpperBandSeries { get; }
            public List<decimal> H4BbMidBandSeries { get; }
            public List<decimal> H4BbLowerBandSeries { get; }
            public List<decimal> H4RsiSeries { get; }
            public List<decimal> H4MacdLineSeries { get; }
            public List<decimal> H4MacdHistogramSeries { get; }
        }
    }
}
