namespace IbSwingTrader.Domain.Settings
{
    public class GetCandidatesSettings
    {
        public int RowsPerScan { get; set; }
        public int FinalTopCandidates { get; set; }
        public bool UseWishListFirst { get; set; }
        public int MaxWishListItems { get; set; }

        public List<ScanCodeSettings> ScanCodes { get; set; } = [];
        public WishListFilterSettings WishListFilter { get; set; } = new();
        public EntryFilterSettings EntryFilter { get; set; } = new();
        public CandidateFilterSettings CandidateFilter { get; set; } = new();
        public FinderSettings Finder { get; set; } = new();
        public PreFilterSettings PreFilter { get; set; } = new();
        public TradePlanSettings TradePlan { get; set; } = new();
    }

    public class ScanCodeSettings
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class WishListFilterSettings
    {
        public decimal MinDollarVolume { get; set; }

        public decimal MaxDistanceTo20dHigh { get; set; }
        public decimal MaxDistanceTo52wHigh { get; set; }

        public decimal MaxDailyMaSignedDistancePct { get; set; }

        public decimal MinWeeklyMaSignedDistancePct { get; set; }

        public decimal MinDailyRsi14 { get; set; }
        public decimal MaxDailyRsi14 { get; set; }

        public decimal MinWeeklyRsi14 { get; set; }

        public decimal MaxDailyMacdLineMinusSignal { get; set; }
    }

    public class EntryFilterSettings
    {
        public decimal MinDollarVolume { get; set; }

        public decimal MaxDistanceTo20dHigh { get; set; }

        public decimal MinDailyMaDelta3 { get; set; }
        public decimal MinDailyRsiDelta3 { get; set; }
        public decimal MinDailyMacdDelta3 { get; set; }
        public int MinDailyTurnSignals { get; set; }

        public decimal MinH4MaDelta3 { get; set; }
        public decimal MinH4RsiDelta3 { get; set; }
        public decimal MinH4MacdDelta3 { get; set; }
        public int MinH4TurnSignals { get; set; }

        public decimal MinCurrentDailyRsi14 { get; set; }
    }

    public class TradePlanSettings
    {
        public int MinimumCandles { get; set; } = 10;
        public int StopLookbackBars { get; set; } = 5;
        public decimal StopBufferMultiplier { get; set; } = 0.99m;
        public decimal FallbackStopMultiplier { get; set; } = 0.97m;
        public decimal RiskRewardRatio { get; set; } = 2.0m;
        public int EntryLookbackHours { get; set; } = 48;
        public int EntryMaLength { get; set; } = 20;
        public int EntryAtrLength { get; set; } = 12;
        public int EntryMacdSignalLookbackBars { get; set; } = 4;
        public int MinimumEntryCandles { get; set; } = 35;
        public decimal EntryMeanReversionWeight { get; set; } = 0.35m;
        public decimal EntryMomentumAtrMultiplier { get; set; } = 0.80m;
        public decimal EntryPullbackAtrFraction { get; set; } = 0.25m;
        public decimal MinimumEntryDiscountPct { get; set; } = 0.0025m;
        public decimal MinimumEntryDiscountAtrFraction { get; set; } = 0.15m;
        public decimal MinimumRiskPct { get; set; } = 0.006m;
        public decimal MinimumRiskAtrMultiplier { get; set; } = 0.90m;
    }

    public class CandidateFilterSettings
    {
        public decimal MinDollarVolume { get; set; } = 1_000_000m;
        public decimal MaxBbMidSignedDistancePct { get; set; } = 0.5m;
        public decimal MaxDistanceTo20dHigh { get; set; } = -20m;
        public decimal MaxAtrRatio { get; set; } = 0.20m;
    }

    public class FinderSettings
    {
        public int CandleCount { get; set; } = 300;
        public int MinimumCandles { get; set; } = 60;
        public int AvgVolumePeriod { get; set; } = 20;
        public int LookbackCalendarDays { get; set; } = 240;
    }

    public class PreFilterSettings
    {
        public string RequiredCurrency { get; set; } = "USD";
        public List<string> DenyList { get; set; } = [];
        public List<string> AllowedStockTypeMarkers { get; set; } = [];
        public List<string> RejectTickersEndingWith { get; set; } = [];
    }
}
