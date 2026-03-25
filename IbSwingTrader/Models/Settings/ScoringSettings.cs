namespace IbSwingTrader.Models.Settings
{
    public class ScoringSettings
    {
        public decimal WeeklyWeight { get; set; } = 0.35m;
        public decimal DailyWeight { get; set; } = 0.35m;
        public decimal EntryWeight { get; set; } = 0.30m;
        public decimal CandidateMinTotalScore { get; set; } = 70m;
        public decimal WishListMinWeeklyScore { get; set; } = 60m;
        public decimal WishListMinDailyScore { get; set; } = 60m;
        public decimal CandidateMinEntryScore { get; set; } = 65m;
        public decimal WishListMaxEntryScore { get; set; } = 64m;
    }
}