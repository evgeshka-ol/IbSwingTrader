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
        public NextDayRankingSettings NextDayRanking { get; set; } = new();
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
        public decimal MaxWeeklyMaSignedDistancePct { get; set; }

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
        public decimal MinCurrentDailyMaSignedDistancePct { get; set; }

        public decimal MinDailyMaDelta3 { get; set; }
        public decimal MinDailyRsiDelta3 { get; set; }
        public decimal MinDailyRsiDelta3Strong { get; set; }
        public decimal MinDailyMacdDelta3 { get; set; }
        public int MinDailyTurnSignals { get; set; }

        public decimal MinH4MaDelta3 { get; set; }
        public decimal MinH4RsiDelta3 { get; set; }
        public decimal MinH4MacdDelta3 { get; set; }
        public int MinH4TurnSignals { get; set; }

        public decimal MinCurrentDailyRsi14 { get; set; }
        public decimal MaxCurrentWeeklyMacdLineMinusSignal { get; set; } = decimal.MaxValue;
    }

    public class TradePlanSettings
    {
        public bool UseCurrentPriceAsEntry { get; set; } = false;
        public int MinimumCandles { get; set; } = 10;
        public int StopLookbackBars { get; set; } = 5;
        public decimal StopBufferMultiplier { get; set; } = 0.99m;
        public decimal FallbackStopMultiplier { get; set; } = 0.97m;
        public decimal RiskRewardRatio { get; set; } = 2.0m;
        public decimal DefaultProfitPct { get; set; } = 0.05m;
        public decimal MinProfitPct { get; set; } = 0.03m;
        public decimal MaxProfitPct { get; set; } = 0.10m;
        public int H4TargetLookbackBars { get; set; } = 24;
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
        public DeepPullbackEntrySettings DeepPullbackEntry { get; set; } = new();
    }

    public class DeepPullbackEntrySettings
    {
        public bool Enabled { get; set; } = true;
        public int MinSignalsRequired { get; set; } = 3;
        public decimal DistanceTo20dHighThreshold { get; set; } = -14m;
        public decimal DailyTrendPositionThreshold { get; set; } = 1m;
        public decimal TrendPositionThreshold { get; set; } = 5m;
        public decimal DailyRsi14Threshold { get; set; } = 46m;
        public decimal EntryDiscountPct { get; set; } = 0.03m;
        public decimal HighAtrEntryDiscountPct { get; set; } = 0.04m;
        public decimal HighAtrRatioThreshold { get; set; } = 3m;
    }

    public class NextDayRankingSettings
    {
        public decimal EntryScoreWeight { get; set; } = 0.35m;
        public decimal CandidateScoreWeight { get; set; } = 0.20m;
        public decimal TrendPositionWeight { get; set; } = 0.15m;
        public decimal DailyTrendPositionWeight { get; set; } = 0.10m;
        public decimal AtrRatioWeight { get; set; } = 0.10m;
        public decimal BbMidWeight { get; set; } = 0.05m;
        public decimal PresetWeight { get; set; } = 0.05m;

        public decimal EntryScoreNormMax { get; set; } = 120m;
        public decimal CandidateScoreNormMax { get; set; } = 140m;
        public decimal TrendPositionNormMax { get; set; } = 10m;
        public decimal DailyTrendPositionNormMax { get; set; } = 6m;
        public decimal AtrRatioNormMax { get; set; } = 4m;
        public decimal BbMidNormMax { get; set; } = 8m;

        public decimal EntryScoreBonusThreshold { get; set; } = 100m;
        public decimal EntryScoreBonus { get; set; } = 0.08m;
        public decimal TrendPositionBonusThreshold { get; set; } = 6m;
        public decimal TrendPositionBonus { get; set; } = 0.05m;
        public decimal DailyTrendPositionBonusThreshold { get; set; } = 3m;
        public decimal DailyTrendPositionBonus { get; set; } = 0.05m;
        public decimal AtrRatioBonusThreshold { get; set; } = 2.8m;
        public decimal AtrRatioBonus { get; set; } = 0.05m;

        public decimal DailyTrendNegativePenaltyThreshold { get; set; } = 0m;
        public decimal DailyTrendNegativePenalty { get; set; } = 0.08m;
        public decimal BbMidNegativePenaltyThreshold { get; set; } = 0m;
        public decimal BbMidNegativePenalty { get; set; } = 0.05m;

        public decimal HotByVolumePresetBonus { get; set; } = 1.0m;
        public decimal MostActivePresetBonus { get; set; } = 0.9m;
        public decimal TopPercGainPresetBonus { get; set; } = 0.7m;
        public decimal TopPercLosePresetBonus { get; set; } = 0.4m;
        public decimal TopOpenPercGainPresetBonus { get; set; } = 0.2m;
        public decimal TopOpenPercLosePresetBonus { get; set; } = 0.1m;
        public decimal DefaultPresetBonus { get; set; } = 0m;
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
