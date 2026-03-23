namespace IbSwingTrader.Models
{
    public class GetCandidatesSettings
    {
        public int RowsPerScan { get; set; }
        public int FinalTopCandidates { get; set; }
        public bool UseWishListFirst { get; set; }
        public int MaxWishListItems { get; set; }
        public List<ScanCodeSettings> ScanCodes { get; set; } = [];
        public TradePlanSettings TradePlan { get; set; } = new();
    }

    public class ScanCodeSettings
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class TradePlanSettings
    {
        public int MinimumCandles { get; set; } = 10;
        public int StopLookbackBars { get; set; } = 5;
        public decimal StopBufferMultiplier { get; set; } = 0.99m;
        public decimal FallbackStopMultiplier { get; set; } = 0.97m;
        public decimal RiskRewardRatio { get; set; } = 2.0m;
    }
}