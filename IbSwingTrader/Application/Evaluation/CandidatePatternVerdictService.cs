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
            var detectedPipeline = DeterminePipeline(series);
            if (detectedPipeline == "Unknown")
            {
                return new CandidatePatternVerdict(
                    detectedPipeline,
                    "Unknown",
                    "Unknown",
                    "Reason=candidate family unavailable");
            }

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
                dailyState.Direction,
                h4State.Direction);

            if (detectedPipeline == "Runaway")
            {
                if (IsRunawayBellUpPattern(bellSignal, dailyState, h4State) &&
                    !IsH4ContradictingDailyBellUp(series, bellSignal, h4State))
                {
                    if (IsVerticalSpikeExpansion(series, bellSignal.Timeframe))
                    {
                        return new CandidatePatternVerdict(
                            detectedPipeline,
                            "None",
                            "Mismatch",
                            $"Reason=BellUp not confirmed on {bellSignal.Timeframe}: isolated terminal spike");
                    }

                    if (HasTerminalMomentumRollover(series, bellSignal.Timeframe))
                    {
                        return new CandidatePatternVerdict(
                            detectedPipeline,
                            "None",
                            "Mismatch",
                            $"Reason=BellUp not confirmed on {bellSignal.Timeframe}: expansion ended in momentum rollover");
                    }

                    return new CandidatePatternVerdict(
                        detectedPipeline,
                        "BellUp",
                        "Match",
                        $"Reason=BellUp confirmed on {bellSignal.Timeframe}");
                }

                return new CandidatePatternVerdict(
                    detectedPipeline,
                    bellSignal.Kind.ToString(),
                    "Mismatch",
                    $"Reason=BellUp not confirmed; detected={bellSignal.Kind}, timeframe={bellSignal.Timeframe}");
            }

            if (IsReversalHookPattern(series, out var diagnostics))
            {
                if (IsH4ContradictingDailyReversalHook(series, h4State, out var h4Diagnostics))
                {
                    return new CandidatePatternVerdict(
                        detectedPipeline,
                        "None",
                        "Mismatch",
                        h4Diagnostics);
                }

                return new CandidatePatternVerdict(
                    detectedPipeline,
                    "ReversalHook",
                    "Match",
                    "Reason=ReversalHook confirmed");
            }

            return new CandidatePatternVerdict(
                detectedPipeline,
                "None",
                "Mismatch",
                diagnostics);
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

        private static string DeterminePipeline(PatternSeries series)
        {
            if (series.CandidateGroup.Equals("Runaway", StringComparison.OrdinalIgnoreCase))
                return "Runaway";

            if (series.CandidateGroup.Equals("Reversal", StringComparison.OrdinalIgnoreCase))
                return "Reversal";

            return "Unknown";
        }

        private static BellPatternSignal ClassifyBellPatternSignal(
            IReadOnlyList<decimal> dailyUpper,
            IReadOnlyList<decimal> dailyMid,
            IReadOnlyList<decimal> dailyLower,
            IReadOnlyList<decimal> h4Upper,
            IReadOnlyList<decimal> h4Mid,
            IReadOnlyList<decimal> h4Lower,
            BollingerFigureDirection dailyDirection,
            BollingerFigureDirection h4Direction)
        {
            var dailyKind = ClassifyBellPatternKindForTimeframe(dailyUpper, dailyMid, dailyLower, dailyDirection);
            var h4Kind = ClassifyBellPatternKindForTimeframe(h4Upper, h4Mid, h4Lower, h4Direction);

            var bellUpSignal = SelectBellPatternSignal(dailyKind, h4Kind, BellPatternKind.BellUp);
            if (bellUpSignal.Kind != BellPatternKind.None)
                return bellUpSignal;

            return SelectBellPatternSignal(dailyKind, h4Kind, BellPatternKind.BellDown);
        }

        private static BellPatternSignal SelectBellPatternSignal(
            BellPatternKind dailyKind,
            BellPatternKind h4Kind,
            BellPatternKind targetKind)
        {
            if (h4Kind == targetKind)
                return new BellPatternSignal(targetKind, BellPatternTimeframe.H4);

            if (dailyKind == targetKind)
                return new BellPatternSignal(targetKind, BellPatternTimeframe.Daily);

            return new BellPatternSignal(BellPatternKind.None, BellPatternTimeframe.None);
        }

        private static BellPatternKind ClassifyBellPatternKindForTimeframe(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower,
            BollingerFigureDirection direction)
        {
            if (!TryCalculateBellPhaseEnvelopes(upper, mid, lower, out var prior, out var recent))
                return BellPatternKind.None;

            if (((IsBellUpEnvelope(prior, recent) &&
                  IsBellUpCurveTurn(upper, mid, lower)) ||
                 IsGradualBellUpLaunch(upper, mid, lower)) &&
                direction != BollingerFigureDirection.Down)
            {
                return BellPatternKind.BellUp;
            }

            if (IsBellDownEnvelope(prior, recent) &&
                IsBellDownCurveTurn(upper, mid, lower) &&
                direction != BollingerFigureDirection.Up)
            {
                return BellPatternKind.BellDown;
            }

            return BellPatternKind.None;
        }

        private static bool TryCalculateBellPhaseEnvelopes(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower,
            out RealBollingerEnvelope prior,
            out RealBollingerEnvelope recent)
        {
            prior = default;
            recent = default;

            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            if (count < 6)
                return false;

            var half = count / 2;
            if (half < 3)
                return false;

            var priorUpper = upper.Take(half).ToList();
            var priorMid = mid.Take(half).ToList();
            var priorLower = lower.Take(half).ToList();
            var recentUpper = upper.Skip(half).ToList();
            var recentMid = mid.Skip(half).ToList();
            var recentLower = lower.Skip(half).ToList();

            return TryCalculateRealBollingerEnvelope(priorUpper, priorMid, priorLower, out prior) &&
                   TryCalculateRealBollingerEnvelope(recentUpper, recentMid, recentLower, out recent);
        }

        private static bool TryCalculateRealBollingerEnvelope(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower,
            out RealBollingerEnvelope envelope)
        {
            envelope = default;

            if (upper.Count < 3 || mid.Count < 3 || lower.Count < 3)
                return false;

            var upperMovePct = CalculateRelativeSlopePct(upper.ToList());
            var midMovePct = CalculateRelativeSlopePct(mid.ToList());
            var lowerMovePct = CalculateRelativeSlopePct(lower.ToList());
            var openPct = CalculateRelativeSlopePct(upper.Zip(lower, (u, l) => u - l).ToList());

            envelope = new RealBollingerEnvelope(
                upperMovePct,
                midMovePct,
                lowerMovePct,
                openPct);
            return true;
        }

        private static bool IsBellUpEnvelope(RealBollingerEnvelope prior, RealBollingerEnvelope recent)
        {
            var midAccelerating = recent.MidMovePct > prior.MidMovePct;
            var openExpanding = recent.OpenPct > prior.OpenPct;
            var meaningfulOpening =
                recent.OpenPct >= 1m &&
                recent.OpenPct >= Math.Abs(recent.MidMovePct) * 0.25m;

            return midAccelerating &&
                   openExpanding &&
                   meaningfulOpening &&
                   recent.UpperMovePct > recent.MidMovePct &&
                   IsLowerBandLaggingForBellUp(recent);
        }

        private static bool IsLowerBandLaggingForBellUp(RealBollingerEnvelope recent)
        {
            return recent.LowerMovePct <= 0m ||
                   recent.LowerMovePct <= recent.MidMovePct * 0.6m;
        }

        private static bool IsGradualBellUpLaunch(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            if (count < 6)
                return false;

            var upperTailSlope = CalculateTailRelativeSlopePct(upper, 4);
            var midTailSlope = CalculateTailRelativeSlopePct(mid, 4);
            var lowerTailSlope = CalculateTailRelativeSlopePct(lower, 4);
            var width = upper
                .TakeLast(count)
                .Zip(lower.TakeLast(count), (u, l) => u - l)
                .ToList();
            var widthTailSlope = CalculateTailRelativeSlopePct(width, 4);

            return midTailSlope > 0m &&
                   upperTailSlope >= midTailSlope + 0.5m &&
                   IsLowerBandLaggingForBellUp(new RealBollingerEnvelope(
                       upperTailSlope,
                       midTailSlope,
                       lowerTailSlope,
                       widthTailSlope)) &&
                   widthTailSlope >= 5m &&
                   width[^1] > width[^2];
        }

        private static bool IsBellDownEnvelope(RealBollingerEnvelope prior, RealBollingerEnvelope recent)
        {
            var midDecelerating = recent.MidMovePct < prior.MidMovePct;
            var openExpanding = recent.OpenPct > prior.OpenPct;

            return midDecelerating &&
                   openExpanding &&
                   recent.LowerMovePct < recent.MidMovePct &&
                   recent.UpperMovePct >= recent.MidMovePct;
        }

        private static bool IsBellUpCurveTurn(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            if (count < 4)
                return false;

            var upperTail = upper.TakeLast(4).ToArray();
            var midTail = mid.TakeLast(4).ToArray();
            var lowerTail = lower.TakeLast(4).ToArray();

            var upperPrevDelta = upperTail[2] - upperTail[1];
            var upperLastDelta = upperTail[3] - upperTail[2];
            var midPrevDelta = midTail[2] - midTail[1];
            var midLastDelta = midTail[3] - midTail[2];
            var lowerPrevDelta = lowerTail[2] - lowerTail[1];
            var lowerLastDelta = lowerTail[3] - lowerTail[2];
            var widthPrevDelta = (upperTail[2] - lowerTail[2]) - (upperTail[1] - lowerTail[1]);
            var widthLastDelta = (upperTail[3] - lowerTail[3]) - (upperTail[2] - lowerTail[2]);

            return upperLastDelta > 0m &&
                   upperLastDelta >= upperPrevDelta &&
                   midLastDelta >= 0m &&
                   midLastDelta >= midPrevDelta &&
                   lowerLastDelta <= lowerPrevDelta &&
                   widthLastDelta > 0m &&
                   widthLastDelta >= widthPrevDelta;
        }

        private static bool IsBellDownCurveTurn(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            if (count < 4)
                return false;

            var upperTail = upper.TakeLast(4).ToArray();
            var midTail = mid.TakeLast(4).ToArray();
            var lowerTail = lower.TakeLast(4).ToArray();

            var upperPrevDelta = upperTail[2] - upperTail[1];
            var upperLastDelta = upperTail[3] - upperTail[2];
            var midPrevDelta = midTail[2] - midTail[1];
            var midLastDelta = midTail[3] - midTail[2];
            var lowerPrevDelta = lowerTail[2] - lowerTail[1];
            var lowerLastDelta = lowerTail[3] - lowerTail[2];
            var widthPrevDelta = (upperTail[2] - lowerTail[2]) - (upperTail[1] - lowerTail[1]);
            var widthLastDelta = (upperTail[3] - lowerTail[3]) - (upperTail[2] - lowerTail[2]);

            return lowerLastDelta < 0m &&
                   lowerLastDelta <= lowerPrevDelta &&
                   midLastDelta <= 0m &&
                   midLastDelta <= midPrevDelta &&
                   upperLastDelta >= upperPrevDelta &&
                   widthLastDelta > 0m &&
                   widthLastDelta >= widthPrevDelta;
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

            var h4RsiTail = CalculateTailSlope(series.H4RsiSeries, 4);
            var h4MacdHistogramTail = CalculateTailSlope(series.H4MacdHistogramSeries, 4);

            return h4RsiTail < -3m &&
                   h4MacdHistogramTail <= 0m;
        }

        private static bool IsH4ContradictingDailyReversalHook(
            PatternSeries series,
            BollingerFigureState h4State,
            out string diagnostics)
        {
            var h4RsiTail = CalculateTailSlope(series.H4RsiSeries, 4);
            var h4MacdHistogramTail = CalculateTailSlope(series.H4MacdHistogramSeries, 4);

            diagnostics =
                $"Reason=H4 does not confirm daily ReversalHook, " +
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

        private static bool IsVerticalSpikeExpansion(
            PatternSeries series,
            BellPatternTimeframe timeframe)
        {
            IReadOnlyList<decimal> upper = timeframe switch
            {
                BellPatternTimeframe.H4 => series.H4BbUpperBandSeries,
                BellPatternTimeframe.Daily => series.DailyBbUpperBandSeries,
                _ => []
            };
            IReadOnlyList<decimal> lower = timeframe switch
            {
                BellPatternTimeframe.H4 => series.H4BbLowerBandSeries,
                BellPatternTimeframe.Daily => series.DailyBbLowerBandSeries,
                _ => []
            };
            IReadOnlyList<decimal> rsi = timeframe switch
            {
                BellPatternTimeframe.H4 => series.H4RsiSeries,
                BellPatternTimeframe.Daily => series.DailyRsiSeries,
                _ => []
            };
            IReadOnlyList<decimal> macdHistogram = timeframe switch
            {
                BellPatternTimeframe.H4 => series.H4MacdHistogramSeries,
                BellPatternTimeframe.Daily => series.DailyMacdHistogramSeries,
                _ => []
            };

            var count = Math.Min(upper.Count, lower.Count);
            if (count < 6 || rsi.Count < count || macdHistogram.Count < count)
                return false;

            var upperDeltas = CalculateDeltas(upper.TakeLast(count).ToList());
            var widthDeltas = CalculateDeltas(upper
                .TakeLast(count)
                .Zip(lower.TakeLast(count), (u, l) => u - l)
                .ToList());
            var rsiDeltas = CalculateDeltas(rsi.TakeLast(count).ToList());
            var histogramDeltas = CalculateDeltas(macdHistogram.TakeLast(count).ToList());

            for (var i = 1; i < upperDeltas.Count; i++)
            {
                var upperJumpDominates =
                    upperDeltas[i] > 0m &&
                    upperDeltas[i] > upperDeltas.Take(i).Select(Math.Abs).Sum();
                var widthJumpDominates =
                    widthDeltas[i] > 0m &&
                    widthDeltas[i] > widthDeltas.Take(i).Select(Math.Abs).Sum();
                var momentumJump =
                    rsiDeltas[i] > 0m &&
                    histogramDeltas[i] > 0m;

                if (upperJumpDominates && widthJumpDominates && momentumJump)
                    return true;
            }

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

        private static bool IsReversalHookPattern(PatternSeries series, out string diagnostics)
        {
            diagnostics = string.Empty;

            var upper = series.DailyBbUpperBandSeries;
            var mid = series.DailyBbMidBandSeries;
            var lower = series.DailyBbLowerBandSeries;
            var rsi = series.DailyRsiSeries;
            var macdLine = series.DailyMacdLineSeries;
            var macdSignal = series.DailyMacdSignalSeries;
            var macdHistogram = series.DailyMacdHistogramSeries;

            if (upper.Count < 4 || mid.Count < 4 || lower.Count < 4 || rsi.Count < 4 || macdHistogram.Count < 4)
            {
                diagnostics = "Reason=insufficient-daily-series";
                return false;
            }

            var lowerDeltas = CalculateDeltas(lower);
            var midDeltas = CalculateDeltas(mid);
            var histogramDeltas = CalculateDeltas(macdHistogram);

            var lowerRecent = lowerDeltas.TakeLast(3).ToList();
            var lowerPrior = lowerDeltas.Take(Math.Max(0, lowerDeltas.Count - 2)).TakeLast(5).ToList();
            var lowerBrokeDown = lowerPrior.Any(x => x < 0m) || lowerDeltas.TakeLast(5).Any(x => x < 0m);
            var lowerHookStrengthPct = CalculateTailRelativeSlopePct(lower, 3);
            var lowerHooked = lowerRecent.Count >= 2 && lowerRecent.Count(x => x >= 0m) >= 2 && lowerRecent[^1] >= 0m;
            var lowerHookStrong = lowerHooked && lowerHookStrengthPct >= 0.8m;

            var midRecent = midDeltas.TakeLast(3).ToList();
            var midPrior = midDeltas.Take(Math.Max(0, midDeltas.Count - 2)).TakeLast(5).ToList();
            var midWorstPrior = midPrior.Count > 0 ? midPrior.Min() : 0m;
            var midRecentSlopePct = CalculateTailRelativeSlopePct(mid, 3);
            var midPriorSlopePct = CalculateSegmentRelativeSlopePct(mid, 3, 3);
            var midDecelerated =
                midRecentSlopePct >= 0m ||
                (midPriorSlopePct < 0m &&
                 midRecentSlopePct < 0m &&
                 Math.Abs(midRecentSlopePct) <= Math.Abs(midPriorSlopePct) * 0.65m);
            var midHooked = midRecent.Count >= 2 &&
                            (midRecent[^1] >= 0m || midRecent[^1] > midWorstPrior || midRecent[^1] >= midRecent[^2]);

            var currentWidth = upper[^1] - lower[^1];
            var previousWidth = upper[^4] - lower[^4];
            var bandCompression = currentWidth < previousWidth;

            var histogramRecent = histogramDeltas.TakeLast(3).ToList();
            var histogramTurnsUp = histogramRecent.Count >= 2 &&
                                   histogramRecent.Count(x => x > 0m) >= 2 &&
                                   macdHistogram[^1] > macdHistogram[^3];

            var macdConverges = false;
            if (macdLine.Count >= 4 && macdSignal.Count >= 4)
            {
                var pairCount = Math.Min(macdLine.Count, macdSignal.Count);
                var macdGap = macdLine
                    .TakeLast(pairCount)
                    .Zip(macdSignal.TakeLast(pairCount), (line, signal) => line - signal)
                    .ToList();

                macdConverges =
                    macdGap.Count >= 3 &&
                    macdGap[^1] > macdGap[^2] &&
                    macdGap[^2] >= macdGap[^3];
            }
            else
            {
                macdConverges = histogramTurnsUp;
            }

            var rsiTurnsUp =
                rsi.Count < 4 ||
                (rsi[^1] > rsi[^2] &&
                 rsi[^1] > rsi.TakeLast(4).Min());

            diagnostics =
                $"LowerBrokeDown={lowerBrokeDown}, " +
                $"LowerHooked={lowerHooked}, " +
                $"LowerHookStrong={lowerHookStrong}, " +
                $"MidHooked={midHooked}, " +
                $"MidDecelerated={midDecelerated}, " +
                $"BandCompression={bandCompression}, " +
                $"MacdHistogramTurnsUp={histogramTurnsUp}, " +
                $"MacdConverges={macdConverges}, " +
                $"RsiTurnsUp={rsiTurnsUp}, " +
                $"LowerHookStrengthPct={lowerHookStrengthPct}, " +
                $"MidRecentSlopePct={midRecentSlopePct}, " +
                $"MidPriorSlopePct={midPriorSlopePct}";

            return lowerBrokeDown &&
                   lowerHooked &&
                   lowerHookStrong &&
                   midHooked &&
                   midDecelerated &&
                   bandCompression &&
                   histogramTurnsUp &&
                   macdConverges &&
                   rsiTurnsUp;
        }

        private static List<decimal> CalculateDeltas(List<decimal> series)
        {
            if (series.Count < 2)
                return [];

            var deltas = new List<decimal>(series.Count - 1);
            for (var i = 1; i < series.Count; i++)
                deltas.Add(series[i] - series[i - 1]);

            return deltas;
        }

        private static decimal CalculateRelativeSlopePct(List<decimal> series)
        {
            if (series.Count < 2)
                return 0m;

            var first = series[0];
            if (first == 0m)
                return series[^1] - first;

            return decimal.Round((series[^1] - first) / Math.Abs(first) * 100m, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal CalculateTailRelativeSlopePct(
            IReadOnlyList<decimal> series,
            int length)
        {
            if (series.Count < 2)
                return 0m;

            var tail = series
                .TakeLast(Math.Min(length, series.Count))
                .ToList();
            return CalculateRelativeSlopePct(tail);
        }

        private static decimal CalculateTailSlope(
            IReadOnlyList<decimal> series,
            int length)
        {
            if (series.Count < 2)
                return 0m;

            var tail = series.TakeLast(Math.Min(length, series.Count)).ToList();
            return tail[^1] - tail[0];
        }

        private static decimal CalculateSegmentRelativeSlopePct(
            IReadOnlyList<decimal> series,
            int lookback,
            int offsetFromEnd)
        {
            var segmentLength = Math.Max(2, lookback);
            if (series.Count < segmentLength + offsetFromEnd)
                return 0m;

            var segment = series
                .Skip(series.Count - offsetFromEnd - segmentLength)
                .Take(segmentLength)
                .ToList();

            return CalculateRelativeSlopePct(segment);
        }

        private readonly record struct RealBollingerEnvelope(
            decimal UpperMovePct,
            decimal MidMovePct,
            decimal LowerMovePct,
            decimal OpenPct);

        private sealed record BellPatternSignal(
            BellPatternKind Kind,
            BellPatternTimeframe Timeframe);

        private enum BellPatternKind
        {
            None = 0,
            BellUp = 1,
            BellDown = 2
        }

        private enum BellPatternTimeframe
        {
            None = 0,
            Daily = 1,
            H4 = 2
        }

        private sealed class PatternSeries
        {
            public PatternSeries(CandidateEvaluationResult candidate)
            {
                CandidateGroup = candidate.CandidateSource.Equals(
                    "SameDayContinuation",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Runaway"
                    : "Reversal";
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
