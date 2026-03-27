namespace IbSwingTrader.Domain.Settings
{
    public class WishListEvaluationSettings
    {
        public int HistoryDaysToLoad { get; set; } = 90;

        public decimal MaxAdditionalDropFromWishListPct { get; set; } = -12m;

        public decimal MinCurrentDailyRsi14 { get; set; } = 30m;

        public decimal MaxCurrentDailyMacdLineMinusSignal { get; set; } = -1.5m;

        public decimal MinWeeklyMaSignedDistancePct { get; set; } = -14m;

        public int MaxDaysInWishListWithoutImprovement { get; set; } = 10;

        public decimal MinDailyMaDelta3ToKeep { get; set; } = 0m;

        public decimal MinDailyRsiDelta3ToKeep { get; set; } = 0m;

        public decimal MinDailyMacdDelta3ToKeep { get; set; } = 0m;
    }
}