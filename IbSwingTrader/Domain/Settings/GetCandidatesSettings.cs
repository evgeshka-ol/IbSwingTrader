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
        public PremarketSummarySettings PremarketSummary { get; set; } = new();
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
        public WishListProfileSettings DeepLaunchProfile { get; set; } = new();
        public WishListProfileSettings ExplosiveBreakoutProfile { get; set; } = new();
    }

    public class WishListProfileSettings
    {
        public bool Enabled { get; set; } = false;
        public decimal MaxDistanceTo20dHigh { get; set; } = decimal.MaxValue;
        public decimal MinDailyBollingerBandWidthPct { get; set; } = decimal.MinValue;
        public decimal MinWeeklyBollingerBandWidthPct { get; set; } = decimal.MinValue;
        public decimal MinCurrentDailyRsi14 { get; set; } = decimal.MinValue;
        public decimal MaxCurrentDailyRsi14 { get; set; } = decimal.MaxValue;
        public decimal MinDailyMaDelta3 { get; set; } = decimal.MinValue;
        public decimal MinDailyRsiDelta3 { get; set; } = decimal.MinValue;
        public decimal MinDailyMacdLineMinusSignal { get; set; } = decimal.MinValue;
        public decimal MaxCurrentWeeklyMacdLineMinusSignal { get; set; } = decimal.MaxValue;
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
        public EntryProfileSettings EarlyReversal { get; set; } = new();
        public EntryProfileSettings HotContinuation { get; set; } = new();
    }

    public class EntryProfileSettings
    {
        public bool Enabled { get; set; } = false;
        public decimal MaxDistanceTo20dHigh { get; set; } = decimal.MaxValue;
        public decimal MinCurrentDailyMaSignedDistancePct { get; set; } = decimal.MinValue;
        public decimal MaxCurrentDailyMaSignedDistancePct { get; set; } = decimal.MaxValue;
        public decimal MinCurrentDailyRsi14 { get; set; } = decimal.MinValue;
        public decimal MaxCurrentDailyRsi14 { get; set; } = decimal.MaxValue;
        public int MinDailyTurnSignals { get; set; } = 1;
        public int MinH4TurnSignals { get; set; } = 1;
        public decimal MaxCurrentWeeklyMacdLineMinusSignal { get; set; } = decimal.MaxValue;
    }

    public class TradePlanSettings
    {
        public bool UseCurrentPriceAsEntry { get; set; } = false;
        public decimal BaselineEntryDiscountPct { get; set; } = 0.003m;
        public int MinimumCandles { get; set; } = 10;
        public int StopLookbackBars { get; set; } = 5;
        public decimal StopBufferMultiplier { get; set; } = 0.99m;
        public decimal FallbackStopMultiplier { get; set; } = 0.97m;
        public decimal StopLimitOffsetPct { get; set; } = 0.0025m;
        public decimal MaxLossPct { get; set; } = 0.10m;
        public bool CapLossToTargetProfitForFastTrades { get; set; } = true;
        public decimal FastTradeProfitPctThreshold { get; set; } = 0.05m;
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
        public MomentumExitSettings MomentumExit { get; set; } = new();
        public StrongMinFirstExitSettings StrongMinFirstExit { get; set; } = new();
        public ExplosiveMinFirstExitSettings ExplosiveMinFirstExit { get; set; } = new();
        public ConstructiveDeepMinFirstSettings ConstructiveDeepMinFirst { get; set; } = new();
        public WeakDeepPullbackExitSettings WeakDeepPullbackExit { get; set; } = new();
        public ExplosiveMaxFirstExitSettings ExplosiveMaxFirstExit { get; set; } = new();
        public ParabolicExpansionExitSettings ParabolicExpansionExit { get; set; } = new();
        public DeepParabolicExpansionExitSettings DeepParabolicExpansionExit { get; set; } = new();
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

    public class MomentumExitSettings
    {
        public bool Enabled { get; set; } = true;
        public int MinSignalsRequired { get; set; } = 3;
        public decimal EntryScoreThreshold { get; set; } = 100m;
        public decimal TrendPositionThreshold { get; set; } = 6m;
        public decimal DailyTrendPositionThreshold { get; set; } = 3m;
        public decimal AtrRatioThreshold { get; set; } = 2.8m;
        public decimal DefaultProfitPct { get; set; } = 0.06m;
        public decimal MinProfitPct { get; set; } = 0.04m;
        public decimal MaxProfitPct { get; set; } = 0.09m;
        public decimal EntryDiscountPct { get; set; } = 0m;
        public decimal ExitPriceBufferPct { get; set; } = 0.001m;
    }

    public class StrongMinFirstExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal DailyTrendPositionThreshold { get; set; } = 2m;
        public decimal TrendPositionThreshold { get; set; } = 4m;
        public decimal MaxAtrRatio { get; set; } = 3m;
        public decimal MaxDistanceTo20dHigh { get; set; } = -5m;
        public decimal DefaultProfitPct { get; set; } = 0.07m;
        public decimal MinProfitPct { get; set; } = 0.05m;
        public decimal MaxProfitPct { get; set; } = 0.12m;
    }

    public class ExplosiveMinFirstExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinDailyTrendPosition { get; set; } = 4m;
        public decimal MinTrendPosition { get; set; } = 8m;
        public decimal MinAtrRatio { get; set; } = 3m;
        public decimal MinDailyRsi14 { get; set; } = 55m;
        public decimal MaxDistanceTo20dHigh { get; set; } = -3m;
        public decimal DefaultProfitPct { get; set; } = 0.085m;
        public decimal MinProfitPct { get; set; } = 0.06m;
        public decimal MaxProfitPct { get; set; } = 0.14m;
        public decimal EntryDiscountPct { get; set; } = 0m;
    }

    public class ConstructiveDeepMinFirstSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinDailyTrendPosition { get; set; } = -4m;
        public decimal MinTrendPosition { get; set; } = 6m;
        public decimal MinAtrRatio { get; set; } = 2.8m;
        public decimal MinDailyRsi14 { get; set; } = 40m;
        public decimal EntryDiscountPct { get; set; } = 0.05m;
        public decimal HighAtrEntryDiscountPct { get; set; } = 0.06m;
        public decimal HighAtrRatioThreshold { get; set; } = 4m;
        public decimal DefaultProfitPct { get; set; } = 0.06m;
        public decimal MinProfitPct { get; set; } = 0.04m;
        public decimal MaxProfitPct { get; set; } = 0.10m;
    }

    public class WeakDeepPullbackExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MaxDailyTrendPosition { get; set; } = 1m;
        public decimal MinAtrRatio { get; set; } = 3m;
        public decimal EntryDiscountPct { get; set; } = 0.06m;
        public decimal HighAtrEntryDiscountPct { get; set; } = 0.07m;
        public decimal HighAtrRatioThreshold { get; set; } = 4m;
        public decimal DefaultProfitPct { get; set; } = 0.04m;
        public decimal MinProfitPct { get; set; } = 0.03m;
        public decimal MaxProfitPct { get; set; } = 0.05m;
        public decimal MaxLossPct { get; set; } = 0.05m;
    }

    public class ExplosiveMaxFirstExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinDailyRsi14 { get; set; } = 60m;
        public decimal MinAtrRatio { get; set; } = 3.5m;
        public decimal MaxVolumeRatio20 { get; set; } = 0.35m;
        public decimal DefaultProfitPct { get; set; } = 0.08m;
        public decimal MinProfitPct { get; set; } = 0.06m;
        public decimal MaxProfitPct { get; set; } = 0.12m;
        public decimal MaxLossPct { get; set; } = 0.05m;
    }

    public class ParabolicExpansionExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinDailyTrendPosition { get; set; } = 10m;
        public decimal MinTrendPosition { get; set; } = 12m;
        public decimal MinAtrRatio { get; set; } = 3m;
        public decimal MinDailyRsi14 { get; set; } = 65m;
        public decimal MaxDistanceTo20dHigh { get; set; } = -15m;
        public decimal MinVolumeRatio20 { get; set; } = 0.8m;
        public decimal DefaultProfitPct { get; set; } = 0.10m;
        public decimal MinProfitPct { get; set; } = 0.08m;
        public decimal MaxProfitPct { get; set; } = 0.18m;
        public decimal EntryDiscountPct { get; set; } = 0m;
        public decimal MaxLossPct { get; set; } = 0.05m;
    }

    public class DeepParabolicExpansionExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinDailyTrendPosition { get; set; } = 6m;
        public decimal MinTrendPosition { get; set; } = 8m;
        public decimal MinAtrRatio { get; set; } = 4m;
        public decimal MinDailyRsi14 { get; set; } = 60m;
        public decimal MinVolumeRatio20 { get; set; } = 0.8m;
        public decimal DefaultProfitPct { get; set; } = 0.08m;
        public decimal MinProfitPct { get; set; } = 0.06m;
        public decimal MaxProfitPct { get; set; } = 0.14m;
        public decimal EntryDiscountPct { get; set; } = 0.02m;
        public decimal MaxLossPct { get; set; } = 0.06m;
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
        public decimal DeeperEntryBonus { get; set; } = 0.10m;
        public decimal MomentumExitPenalty { get; set; } = 0.05m;
        public decimal StrongMinFirstBonus { get; set; } = 0.08m;
        public decimal ExplosiveMinFirstBonus { get; set; } = 0.07m;
        public decimal ConstructiveDeepMinFirstBonus { get; set; } = 0.05m;
        public decimal WeakDeepPullbackPenalty { get; set; } = 0.12m;
        public decimal ParabolicExpansionBonus { get; set; } = 0.10m;
        public decimal DeepParabolicExpansionBonus { get; set; } = 0.06m;
        public decimal DeepLaunchBonus { get; set; } = 0.08m;
        public decimal ExplosiveBreakoutBonus { get; set; } = 0.10m;
        public decimal EarlyReversalBonus { get; set; } = 0.10m;
        public decimal EarlyReversalMaxTrendPosition { get; set; } = 2m;
        public decimal EarlyReversalMaxDailyTrendPosition { get; set; } = 2m;
        public decimal EarlyReversalMaxDistanceTo20dHigh { get; set; } = -8m;
        public decimal EarlyReversalMinDailyRsi14 { get; set; } = 38m;
        public decimal EarlyReversalMaxDailyRsi14 { get; set; } = 52m;
        public decimal EarlyReversalMinAtrRatio { get; set; } = 2.4m;
        public decimal EarlyReversalMaxVolumeRatio20 { get; set; } = 0.40m;

        public decimal DailyTrendNegativePenaltyThreshold { get; set; } = 0m;
        public decimal DailyTrendNegativePenalty { get; set; } = 0.08m;
        public decimal BbMidNegativePenaltyThreshold { get; set; } = 0m;
        public decimal BbMidNegativePenalty { get; set; } = 0.05m;
        public decimal LateExtensionDistanceTo20dHighThreshold { get; set; } = -3m;
        public decimal LateExtensionDailyRsi14Threshold { get; set; } = 70m;
        public decimal LateExtensionPenalty { get; set; } = 0.10m;
        public decimal PatternConstructiveLaunchBonus { get; set; } = 0.08m;
        public decimal PatternH4TrendBonus { get; set; } = 0.05m;
        public decimal PatternExhaustionPenalty { get; set; } = 0.10m;
        public decimal PatternDailyMaSlopeThreshold { get; set; } = 2.0m;
        public decimal PatternDailyRsiSlopeThreshold { get; set; } = 8.0m;
        public decimal PatternH4MaSlopeThreshold { get; set; } = 1.0m;
        public decimal PatternH4RsiSlopeThreshold { get; set; } = 10.0m;
        public decimal PatternExhaustionH4RsiThreshold { get; set; } = 78.0m;
        public decimal LateContinuationDailyRsi14Threshold { get; set; } = 60.0m;
        public decimal LateContinuationDistanceTo20dHighThreshold { get; set; } = -6.0m;
        public decimal LateContinuationTrendPositionThreshold { get; set; } = 4.0m;
        public decimal LateContinuationBbMidThreshold { get; set; } = 3.0m;
        public decimal LateContinuationPenalty { get; set; } = 0.14m;
        public decimal ResearchLikeDistanceTo20dHighThreshold { get; set; } = -10.0m;
        public decimal ResearchLikeMaxDailyRsi14 { get; set; } = 58.0m;
        public decimal ResearchLikeMinAtrRatio { get; set; } = 2.2m;
        public decimal ResearchLikeMaxTrendPosition { get; set; } = 1.5m;
        public decimal ResearchLikeMaxBbMid { get; set; } = 1.0m;
        public decimal ResearchLikeBonus { get; set; } = 0.12m;
        public decimal ResearchLikeStrongPatternBonus { get; set; } = 0.08m;

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
        public decimal MinPlannedProfitPct { get; set; } = 3.0m;
    }

    public class PremarketSummarySettings
    {
        public bool Enabled { get; set; } = true;
        public int MaxItems { get; set; } = 5;
        public decimal MinEntryScore { get; set; } = 30m;
        public decimal MinPlannedProfitPct { get; set; } = 3.0m;
    }

    public class FinderSettings
    {
        public int CandleCount { get; set; } = 300;
        public int MinimumCandles { get; set; } = 60;
        public int AvgVolumePeriod { get; set; } = 20;
        public int LookbackCalendarDays { get; set; } = 240;
        public int ContractResolveTimeoutSeconds { get; set; } = 45;
        public int ContractResolveMaxAttempts { get; set; } = 2;
    }

    public class PreFilterSettings
    {
        public string RequiredCurrency { get; set; } = "USD";
        public List<string> DenyList { get; set; } = [];
        public List<string> AllowedStockTypeMarkers { get; set; } = [];
        public List<string> RejectTickersEndingWith { get; set; } = [];
    }
}
