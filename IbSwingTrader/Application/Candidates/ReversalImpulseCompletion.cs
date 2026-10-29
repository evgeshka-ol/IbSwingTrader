namespace IbSwingTrader.Application.Candidates
{
    public readonly record struct ReversalUpperTouch(int BarsAgo, decimal High, decimal Upper, string Source)
    {
        public string Diagnostics => FormattableString.Invariant(
            $"UpperTouchBarsAgo={BarsAgo}, UpperTouchHigh={High}, UpperTouchUpper={Upper}, UpperTouchSource={Source}");
    }

    public sealed record ReversalCompletedImpulse(string Timeframe, DateTime SignalBar,
        DateTime TouchBar, DateTime ConfirmedAt, decimal High, decimal Upper, string Source)
    {
        public string Diagnostics => FormattableString.Invariant(
            $"ReversalImpulseCompleted(Timeframe={Timeframe}, SignalBar={SignalBar:O}, TouchBar={TouchBar:O}, ConfirmedAt={ConfirmedAt:O}, High={High}, Upper={Upper}, Source={Source})");
    }

    public sealed record ReversalFrameEpisode(bool Recognized, string Timeframe, string Phase,
        string Reason, DateTime? SignalBar, ReversalCompletedImpulse? Completion);

    public static class ReversalImpulseCompletion
    {
        // Tail-aligned completed native series. Each high is compared with its OWN band's value.
        public static ReversalUpperTouch? FindTouch(IReadOnlyList<decimal> close,
            IReadOnlyList<decimal> mid, IReadOnlyList<decimal> upper, int barsSinceSignal,
            IReadOnlyList<decimal>? high = null)
        {
            var count = Math.Min(close.Count, Math.Min(mid.Count, upper.Count));
            if (barsSinceSignal < 0 || barsSinceSignal >= count) return null;
            var useHigh = high != null && high.Count >= count;
            for (var i = count - 1 - barsSinceSignal; i < count; i++)
            {
                var c = close[close.Count - count + i];
                var m = mid[mid.Count - count + i];
                var u = upper[upper.Count - count + i];
                var h = useHigh ? high![high.Count - count + i] : c;
                var validHigh = useHigh && h >= c && h > 0m;
                var observed = validHigh ? h : c;
                if (m > 0m && u > m && observed >= u)
                    return new(count - 1 - i, observed, u, validHigh ? "CompletedHigh" : "CompletedClose");
            }
            return null;
        }

        public static ReversalCompletedImpulse? FromClosedBar(string timeframe, DateTime signalBar,
            DateTime touchBar, ReversalUpperTouch touch, DateTime decisionAt)
        {
            if (timeframe is not ("D1" or "H4") || touch.Source is not ("CompletedHigh" or "CompletedClose") ||
                touch.Upper <= 0m || touch.High < touch.Upper) return null;
            var end = timeframe == "D1" ? touchBar.Date.AddHours(16)
                : timeframe == "H4" ? touchBar.AddHours(4) : DateTime.MaxValue;
            if (touchBar < signalBar || end > decisionAt) return null;
            return new(timeframe, signalBar, touchBar, end, touch.High, touch.Upper, touch.Source);
        }

        public static bool BlocksDaily(DateTime? dailySignalBar, ReversalCompletedImpulse? h4) =>
            dailySignalBar.HasValue && h4 is { Timeframe: "H4" } &&
            h4.SignalBar >= dailySignalBar.Value && h4.TouchBar >= h4.SignalBar;

        public static ReversalFrameEpisode ApplyH4Completion(ReversalFrameEpisode daily, ReversalFrameEpisode h4)
        {
            if (!daily.Recognized || daily.Phase is not ("Active" or "MidCrossed" or "Stalled") ||
                !h4.Recognized || h4.Phase != "Completed" || !BlocksDaily(daily.SignalBar, h4.Completion))
                return daily;
            return daily with
            {
                Phase = "Completed",
                Reason = $"D1=Completed, NotReady=H4-impulse-already-completed-in-D1-episode; " +
                    $"{h4.Completion!.Diagnostics}; ObservedDailyEpisode=({daily.Reason})"
            };
        }
    }
}
