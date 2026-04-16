namespace IbSwingTrader.Domain.Settings
{
    public class CleanUpSettings
    {
        public bool RemoveEvaluatedCandidates { get; set; } = true;

        public List<string> CandidateOutcomesToRemove { get; set; } =
        [
            "Loss",
            "Win",
            "NoEntry",
            "NoData",
            "NoDataAfterScan",
            "InsufficientFutureData"
        ];

        public bool RemoveStaleWishListItems { get; set; } = true;

        public bool RemoveEvaluationReportRows { get; set; } = true;

        public List<string> EvaluationOutcomesToRemove { get; set; } =
        [
            "NoData",
            "NoDataAfterScan",
            "InsufficientFutureData"
        ];

        public int RemoveWishListWithoutTargetOlderThanDays { get; set; } = 14;

        public int RemoveWishListPastExpectedTargetGraceDays { get; set; } = 2;

        public bool DeleteLogsOlderThanCutoff { get; set; } = true;

        public bool DeleteLegacyCandidatesOlderThanCutoff { get; set; } = true;

        public bool DeleteLegacyEvaluationsOlderThanCutoff { get; set; } = true;

        public int FileRetentionDays { get; set; } = 30;
    }
}
