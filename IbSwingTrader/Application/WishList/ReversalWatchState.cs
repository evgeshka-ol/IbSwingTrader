namespace IbSwingTrader.Application.WishList
{
    // Saved separately from active wishlist.csv so consumed hooks do not require
    // recurring broker scans and cannot be forgotten by removing their CSV rows.
    public sealed class ReversalWatchState
    {
        public DateTime? LastDailyBar { get; set; }
        public DateTime? LastH4Bar { get; set; }
        public DateTime? LastHookAt { get; set; }
        public DateTime? CycleStartedAt { get; set; }
        public bool AwaitingNewDrop { get; set; }
        public bool LeftDropZoneSinceHook { get; set; }
        public bool CanStartNewCycle => !AwaitingNewDrop || LeftDropZoneSinceHook;

        public static bool IsDeepBelowMid(decimal price, decimal mid, decimal minimumPct) =>
            price > 0m && mid > 0m && minimumPct > 0m && minimumPct < 100m &&
            price <= mid * (1m - minimumPct / 100m);

        public void StartCycle(DateTime at)
        {
            CycleStartedAt = at;
            AwaitingNewDrop = false;
            LeftDropZoneSinceHook = false;
        }

        public bool ObserveHook(DateTime confirmedAt)
        {
            if (LastHookAt.HasValue && confirmedAt <= LastHookAt.Value)
                return false;
            LastHookAt = confirmedAt;
            if (!CycleStartedAt.HasValue || confirmedAt > CycleStartedAt.Value)
            {
                AwaitingNewDrop = true;
                LeftDropZoneSinceHook = false;
            }
            return true;
        }

        public void ObservePosition(DateTime at, bool deepBelowMid)
        {
            if (AwaitingNewDrop && LastHookAt.HasValue && at >= LastHookAt.Value && !deepBelowMid)
                LeftDropZoneSinceHook = true;
        }
    }
}
