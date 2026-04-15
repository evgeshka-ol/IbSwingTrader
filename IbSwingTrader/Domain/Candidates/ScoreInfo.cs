namespace IbSwingTrader.Domain.Candidates
{
    public class ScoreInfo
    {
        public decimal Score { get; set; }

        public decimal? NextDayRank { get; set; }

        public decimal? WeeklyScore { get; set; }

        public decimal? DailyScore { get; set; }

        public decimal? EntryScore { get; set; }
    }
}
