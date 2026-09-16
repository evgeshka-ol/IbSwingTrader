namespace IbSwingTrader.Application.Candidates
{
    public enum BellPatternKind
    {
        None,
        BellUp,
        BellDown,
        Triangle
    }

    public enum BellPatternTimeframe
    {
        None,
        H4,
        Daily
    }

    public readonly record struct BellPatternSignal(
        BellPatternKind Kind,
        BellPatternTimeframe Timeframe);

    public readonly record struct RealBollingerEnvelope(
        decimal UpperMovePct,
        decimal MidMovePct,
        decimal LowerMovePct,
        decimal OpenPct);

    // Shared Bell/ReversalHook pattern classification, previously duplicated byte-for-byte
    // between CandidateFinder.cs (live scan path) and CandidatePatternVerdictService.cs
    // (offline evaluation/backtest path). The two copies had already drifted apart before
    // this extraction:
    //   - IsReversalHookPattern: the live copy required lowerHookFresh and priceTurnsTowardMid
    //     in addition to everything the offline copy checked; the offline copy silently
    //     skipped both. This class uses the live (stricter) version as canonical.
    //   - TryCalculateRealBollingerEnvelope: the live copy normalizes upper/mid/lower slope %
    //     against a single shared base (mid's first value) and derives OpenPct as
    //     upperMovePct - lowerMovePct; the offline copy normalized each series against its own
    //     first value and computed OpenPct as an independent slope of the width series. This
    //     class uses the live version as canonical.
    public static class BellPatternClassifier
    {
        // Floors on the mid-band's own tail slope for IsGradualBellUpLaunch, separate per
        // timeframe since a 4-bar H4 window (~16-20h) and a 4-bar Daily window (4 sessions)
        // move on very different scales. Picked from real 2026-09-04 data: NVDA (confirmed
        // false positive - no real BellUp shape, just an ordinary choppy uptrend) had
        // midTailSlope 0.19%(H4)/0.5%(Daily), while the weakest genuine BellUp case that day
        // (CMBT, H4-only) had 0.43%(H4); the weakest genuine Daily case (HAFN) had 2.8%(Daily).
        // A plain ">0m" check let NVDA's near-flat mid band through on both timeframes. Not
        // AUC-validated, just an eyeballed cutoff between the false positive and the weakest
        // true positive on each timeframe - see feedback_rigor_before_recalibrating memory.
        private const decimal MinGradualLaunchMidTailSlopePctH4 = 0.3m;
        private const decimal MinGradualLaunchMidTailSlopePctDaily = 1.0m;

        public static List<decimal> CalculateDeltas(List<decimal> series)
        {
            if (series.Count < 2)
                return [];

            var deltas = new List<decimal>(series.Count - 1);
            for (var i = 1; i < series.Count; i++)
                deltas.Add(series[i] - series[i - 1]);

            return deltas;
        }

        public static decimal CalculateSlope(List<decimal> series)
            => series.Count >= 2
                ? decimal.Round(series[^1] - series[0], 2, MidpointRounding.AwayFromZero)
                : 0m;

        public static decimal CalculateRelativeSlopePct(List<decimal> series)
        {
            if (series.Count < 2)
                return 0m;

            var first = series[0];
            if (first == 0m)
                return CalculateSlope(series);

            return decimal.Round((series[^1] - first) / Math.Abs(first) * 100m, 2, MidpointRounding.AwayFromZero);
        }

        public static decimal CalculateTailSlope(List<decimal> series, int lookback)
        {
            if (series.Count < 2)
                return 0m;

            var tail = series.TakeLast(Math.Max(2, lookback)).ToList();
            return decimal.Round(tail[^1] - tail[0], 2, MidpointRounding.AwayFromZero);
        }

        public static decimal CalculateTailRelativeSlopePct(List<decimal> series, int lookback)
        {
            if (series.Count < 2)
                return 0m;

            var tail = series.TakeLast(Math.Max(2, lookback)).ToList();
            var first = tail[0];
            if (first == 0m)
                return CalculateTailSlope(series, lookback);

            return decimal.Round((tail[^1] - first) / Math.Abs(first) * 100m, 2, MidpointRounding.AwayFromZero);
        }

        public static decimal CalculateSegmentRelativeSlopePct(
            List<decimal> series,
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

            if (segment.Count < 2)
                return 0m;

            var first = segment[0];
            if (first == 0m)
                return CalculateTailSlope(segment, segmentLength);

            return decimal.Round((segment[^1] - first) / Math.Abs(first) * 100m, 2, MidpointRounding.AwayFromZero);
        }

        public static bool TryCalculateRealBollingerEnvelope(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower,
            out RealBollingerEnvelope envelope)
        {
            envelope = default;

            if (upper.Count < 4 || mid.Count < 4 || lower.Count < 4)
                return false;

            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            var upperTail = upper.TakeLast(count).ToArray();
            var midTail = mid.TakeLast(count).ToArray();
            var lowerTail = lower.TakeLast(count).ToArray();
            var baseMid = midTail[0];

            if (baseMid <= 0m)
                return false;

            var upperMovePct = (upperTail[^1] - upperTail[0]) / baseMid * 100m;
            var midMovePct = (midTail[^1] - midTail[0]) / baseMid * 100m;
            var lowerMovePct = (lowerTail[^1] - lowerTail[0]) / baseMid * 100m;

            envelope = new RealBollingerEnvelope(
                upperMovePct,
                midMovePct,
                lowerMovePct,
                upperMovePct - lowerMovePct);
            return true;
        }

        public static bool TryCalculateBellPhaseEnvelopes(
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

        public static bool IsLowerBandLaggingForBellUp(RealBollingerEnvelope recent)
        {
            return recent.LowerMovePct <= 0m ||
                   recent.LowerMovePct <= recent.MidMovePct * 0.6m;
        }

        public static bool IsBellUpEnvelope(RealBollingerEnvelope prior, RealBollingerEnvelope recent)
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

        public static bool IsBellDownEnvelope(RealBollingerEnvelope prior, RealBollingerEnvelope recent)
        {
            var midDecelerating = recent.MidMovePct < prior.MidMovePct;
            var openExpanding = recent.OpenPct > prior.OpenPct;

            return midDecelerating &&
                   openExpanding &&
                   recent.LowerMovePct < recent.MidMovePct &&
                   recent.UpperMovePct >= recent.MidMovePct;
        }

        public static bool IsGradualBellUpLaunch(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower,
            BellPatternTimeframe timeframe)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            if (count < 6)
                return false;

            var upperTailSlope = CalculateTailRelativeSlopePct(upper.ToList(), 4);
            var midTailSlope = CalculateTailRelativeSlopePct(mid.ToList(), 4);
            var lowerTailSlope = CalculateTailRelativeSlopePct(lower.ToList(), 4);
            var width = upper
                .TakeLast(count)
                .Zip(lower.TakeLast(count), (u, l) => u - l)
                .ToList();
            var widthTailSlope = CalculateTailRelativeSlopePct(width, 4);
            var minMidTailSlope = timeframe == BellPatternTimeframe.H4
                ? MinGradualLaunchMidTailSlopePctH4
                : MinGradualLaunchMidTailSlopePctDaily;

            return midTailSlope >= minMidTailSlope &&
                   upperTailSlope >= midTailSlope + 0.5m &&
                   IsLowerBandLaggingForBellUp(new RealBollingerEnvelope(
                       upperTailSlope,
                       midTailSlope,
                       lowerTailSlope,
                       widthTailSlope)) &&
                   widthTailSlope >= 5m &&
                   width[^1] > width[^2];
        }

        public static bool IsExplosiveBellUpExpansion(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            if (count < 6)
                return false;

            var upperTailSlope = CalculateTailRelativeSlopePct(upper.ToList(), 4);
            var midTailSlope = CalculateTailRelativeSlopePct(mid.ToList(), 4);
            var width = upper
                .TakeLast(count)
                .Zip(lower.TakeLast(count), (u, l) => u - l)
                .ToList();
            var widthTailSlope = CalculateTailRelativeSlopePct(width, 4);

            return upperTailSlope >= 8m &&
                   midTailSlope >= 5m &&
                   widthTailSlope >= 8m &&
                   upper[^1] > upper[^2] &&
                   mid[^1] > mid[^2] &&
                   width[^1] > width[^2];
        }

        public static bool IsBellUpCurveTurn(
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

        public static bool IsBellDownCurveTurn(
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

        public static BellPatternKind ClassifyBellPatternKindForTimeframe(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower,
            BollingerFigureDirection direction,
            BellPatternTimeframe timeframe)
        {
            if (!TryCalculateBellPhaseEnvelopes(upper, mid, lower, out var prior, out var recent))
                return BellPatternKind.None;

            if (((IsBellUpEnvelope(prior, recent) &&
                  IsBellUpCurveTurn(upper, mid, lower)) ||
                 IsGradualBellUpLaunch(upper, mid, lower, timeframe) ||
                 IsExplosiveBellUpExpansion(upper, mid, lower)) &&
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

        public static BellPatternSignal SelectBellPatternSignal(
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

        public static bool IsVerticalSpikeExpansion(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> lower,
            IReadOnlyList<decimal> rsi,
            IReadOnlyList<decimal> macdHistogram)
        {
            var count = Math.Min(upper.Count, lower.Count);
            if (count < 6 || rsi.Count < count || macdHistogram.Count < count)
                return false;

            var upperDeltas = CalculateDeltas(upper.TakeLast(count).ToList());
            var width = upper
                .TakeLast(count)
                .Zip(lower.TakeLast(count), (u, l) => u - l)
                .ToList();
            var widthDeltas = CalculateDeltas(width);
            var rsiDeltas = CalculateDeltas(rsi.TakeLast(count).ToList());
            var histogramDeltas = CalculateDeltas(macdHistogram.TakeLast(count).ToList());

            for (var i = 1; i < upperDeltas.Count; i++)
            {
                var priorUpperMoveSum = upperDeltas.Take(i).Select(Math.Abs).Sum();
                var priorWidthMoveSum = widthDeltas.Take(i).Select(Math.Abs).Sum();
                var upperJumpDominates = upperDeltas[i] > 0m && upperDeltas[i] > priorUpperMoveSum;
                var widthJumpDominates = widthDeltas[i] > 0m && widthDeltas[i] > priorWidthMoveSum;
                var momentumJump = rsiDeltas[i] > 0m && histogramDeltas[i] > 0m;

                if (upperJumpDominates && widthJumpDominates && momentumJump)
                    return true;
            }

            return false;
        }

        public static bool IsBelowPreviousClosedDailyMid(
            IReadOnlyList<decimal> dailyCloseSeries,
            IReadOnlyList<decimal> dailyBbMidBandSeries)
        {
            if (dailyCloseSeries.Count == 0 ||
                dailyBbMidBandSeries.Count == 0 ||
                dailyCloseSeries.Count != dailyBbMidBandSeries.Count)
            {
                return false;
            }

            return dailyCloseSeries[^1] < dailyBbMidBandSeries[^1];
        }

        public static bool IsPriceTurningTowardDailyMid(
            IReadOnlyList<decimal> dailyCloseSeries,
            IReadOnlyList<decimal> dailyBbMidBandSeries)
        {
            var count = Math.Min(dailyCloseSeries.Count, dailyBbMidBandSeries.Count);
            if (count < 4)
                return false;

            var alignedClose = dailyCloseSeries.TakeLast(count).ToList();
            var alignedMid = dailyBbMidBandSeries.TakeLast(count).ToList();
            if (alignedClose[^1] >= alignedMid[^1])
                return false;

            var latestClose = alignedClose[^1];
            var previousClose = alignedClose[^2];
            var recentLowBeforeLatest = alignedClose
                .Take(count - 1)
                .TakeLast(4)
                .Min();
            var priceStoppedFalling =
                latestClose >= previousClose ||
                latestClose > recentLowBeforeLatest;

            var distances = alignedClose
                .Zip(alignedMid, (c, m) => m - c)
                .Where(x => x > 0m)
                .ToList();
            if (distances.Count < 3)
                return false;

            var distanceCompressing =
                distances[^1] < distances[^2] &&
                distances[^1] < distances[^3];

            return priceStoppedFalling && distanceCompressing;
        }

        // Canonical (live-scanner) version: requires lowerHookFresh and priceTurnsTowardMid in
        // addition to everything the pre-extraction offline copy checked.
        public static bool IsReversalHookPattern(
            IReadOnlyList<decimal> dailyCloseSeries,
            List<decimal> upper,
            List<decimal> mid,
            List<decimal> lower,
            List<decimal> rsi,
            List<decimal> macdLine,
            List<decimal> macdSignal,
            List<decimal> macdHistogram,
            out string diagnostics)
        {
            diagnostics = string.Empty;

            if (!IsBelowPreviousClosedDailyMid(dailyCloseSeries, mid))
            {
                diagnostics = "Reason=not-below-daily-mid";
                return false;
            }

            if (upper.Count < 6 || mid.Count < 6 || lower.Count < 6 || macdHistogram.Count < 4)
            {
                diagnostics =
                    $"Reason=not-enough-daily-rows, " +
                    $"UpperCount={upper.Count}, MidCount={mid.Count}, LowerCount={lower.Count}, " +
                    $"MacdHistogramCount={macdHistogram.Count}";
                return false;
            }

            var lowerDeltas = CalculateDeltas(lower);
            var midDeltas = CalculateDeltas(mid);
            var histogramDeltas = CalculateDeltas(macdHistogram);

            var lowerRecent = lowerDeltas.TakeLast(3).ToList();
            var lowerPrior = lowerDeltas.Take(Math.Max(0, lowerDeltas.Count - 2)).TakeLast(5).ToList();
            var lowerBrokeDown = lowerPrior.Any(x => x < 0m) || lowerDeltas.TakeLast(5).Any(x => x < 0m);
            var lowerHookStrengthPct = CalculateTailRelativeSlopePct(lower, 3);
            var lowerHooked =
                lowerRecent.Count >= 2 &&
                lowerRecent.Count(x => x >= 0m) >= 2 &&
                lowerRecent[^1] >= 0m;
            var lowerHookStrong = lowerHooked && lowerHookStrengthPct >= 0.8m;
            var lowerHookEmerging =
                lowerRecent.Count >= 3 &&
                lowerRecent[^1] < 0m &&
                lowerRecent[^1] > lowerRecent[^2] &&
                lowerRecent[^2] > lowerRecent[^3];
            var lowerHookTurning =
                lowerRecent.Count >= 3 &&
                lowerRecent[^1] >= 0m &&
                lowerRecent[^2] < 0m &&
                lowerRecent[^2] > lowerRecent[^3];
            var lowerHookFresh =
                lowerRecent.Count >= 2 &&
                lowerRecent.Take(lowerRecent.Count - 1).Any(x => x < 0m);

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
            var midHooked =
                midRecent.Count >= 2 &&
                (midRecent[^1] >= 0m ||
                 midRecent[^1] > midWorstPrior ||
                 midRecent[^1] >= midRecent[^2]);

            var currentWidth = upper[^1] - lower[^1];
            var previousWidth = upper[^4] - lower[^4];
            var bandCompression = currentWidth < previousWidth;

            var histogramRecent = histogramDeltas.TakeLast(3).ToList();
            var histogramTurnsUp =
                histogramRecent.Count >= 2 &&
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
            var priceTurnsTowardMid = IsPriceTurningTowardDailyMid(dailyCloseSeries, mid);
            var midContextOk = midHooked && (midDecelerated || lowerHookTurning);

            diagnostics =
                $"LowerBrokeDown={lowerBrokeDown}, " +
                $"LowerHooked={lowerHooked}, " +
                $"LowerHookStrong={lowerHookStrong}, " +
                $"LowerHookEmerging={lowerHookEmerging}, " +
                $"LowerHookTurning={lowerHookTurning}, " +
                $"LowerHookFresh={lowerHookFresh}, " +
                $"MidHooked={midHooked}, " +
                $"MidDecelerated={midDecelerated}, " +
                $"MidContextOk={midContextOk}, " +
                $"PriceTurnsTowardMid={priceTurnsTowardMid}, " +
                $"BandCompression={bandCompression}, " +
                $"MacdHistogramTurnsUp={histogramTurnsUp}, " +
                $"MacdConverges={macdConverges}, " +
                $"RsiTurnsUp={rsiTurnsUp}, " +
                $"LowerHookStrengthPct={lowerHookStrengthPct}, " +
                $"MidRecentSlopePct={midRecentSlopePct}, " +
                $"MidPriorSlopePct={midPriorSlopePct}";

            return lowerBrokeDown &&
                   (lowerHookStrong || lowerHookEmerging || lowerHookTurning) &&
                   lowerHookFresh &&
                   midContextOk &&
                   priceTurnsTowardMid &&
                   bandCompression &&
                   (histogramTurnsUp || macdConverges || lowerHookEmerging) &&
                   rsiTurnsUp;
        }
    }
}
