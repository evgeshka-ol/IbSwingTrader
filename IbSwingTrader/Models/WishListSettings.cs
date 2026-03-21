namespace IbSwingTrader.Models
{
    public class WishListSettings
    {
        public bool TouchStopIsNotRemoval { get; set; } = true;
        public bool RemoveIfTargetReached { get; set; } = true;
        public bool RemoveIfStrongBreakdown { get; set; } = true;
        public decimal StrongBreakdownPctBelowStop { get; set; } = 3m;
        public int MinClosesBelowStopToRemove { get; set; } = 2;
        public int MaxAgeDays { get; set; } = 20;
    }
}