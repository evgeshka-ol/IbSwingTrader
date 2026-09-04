namespace IbSwingTrader.Domain.Settings
{
    public class GetCandidatesSettings
    {
        public int RowsPerScan { get; set; }
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
        public ReversalRecoveryExitSettings ReversalRecoveryExit { get; set; } = new();
        public ExplosiveMaxFirstExitSettings ExplosiveMaxFirstExit { get; set; } = new();
        public ParabolicExpansionExitSettings ParabolicExpansionExit { get; set; } = new();
        public DeepParabolicExpansionExitSettings DeepParabolicExpansionExit { get; set; } = new();
        public ResearchLikeExitSettings ResearchLikeExit { get; set; } = new();
        public AiReferenceTradePlanSettings AiReferenceTradePlan { get; set; } = new();
        public SeriesEntryProfileSettings SeriesEntryProfile { get; set; } = new();
        public H4BollingerEntrySettings H4BollingerEntry { get; set; } = new();
    }

    public class SeriesEntryProfileSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal LossLikeAdverseMoveDiscountPct { get; set; } = 0.09m;
        public decimal WaitPullbackDiscountPct { get; set; } = 0.08m;
        public decimal ConfirmFirstDiscountPct { get; set; } = 0.06m;
        public decimal AvoidEarlySpikeDiscountPct { get; set; } = 0.07m;
        public decimal FastContinuationMaxDiscountPct { get; set; } = 0.012m;
        public decimal ImmediateContinuationMaxDiscountPct { get; set; } = 0.0m;
        public decimal ImmediateContinuationMinAtrRatio { get; set; } = 3.0m;
        public decimal ImmediateContinuationMinDailyMaDistancePct { get; set; } = 0.0m;
        public decimal ImmediateContinuationMinH4MaDistancePct { get; set; } = 0.0m;
        public decimal ImmediateContinuationMinH4Rsi { get; set; } = 58.0m;
        public decimal ShallowContinuationMaxDiscountPct { get; set; } = 0.008m;
        public decimal ModeratePullbackMaxDiscountPct { get; set; } = 0.018m;
        public decimal DeepPullbackDiscountPct { get; set; } = 0.07m;
        public decimal DeepAdverseContinuationDiscountPct { get; set; } = 0.08m;
        public decimal DeepAdverseContinuationMinAtrRatio { get; set; } = 3.0m;
        public decimal DeepAdverseContinuationMinDailyMaDistancePct { get; set; } = 5.0m;
        public decimal DeepAdverseContinuationMaxH4MidSlopePct { get; set; } = -8.0m;
        public decimal DeepAdverseContinuationMaxH4RsiSlope { get; set; } = -4.0m;
        public decimal DeepAdverseContinuationMaxH4MacdSlope { get; set; } = -0.50m;
        public decimal LateSpikeAvoidDiscountPct { get; set; } = 0.09m;
        public decimal DailyStrongSlopeThreshold { get; set; } = 10.0m;
        public decimal DailyRsiSlopeThreshold { get; set; } = 8.0m;
        public decimal H4ContinuationMidSlopeThreshold { get; set; } = 8.0m;
        public decimal H4ContinuationRsiSlopeThreshold { get; set; } = 12.0m;
        public decimal H4ContinuationMacdSlopeThreshold { get; set; } = 0.12m;
        public decimal H4WeakSlopeThreshold { get; set; } = -1.0m;
        public decimal H4MacdWeakDeltaThreshold { get; set; } = -0.10m;
        public decimal DailyOverheatedRsiThreshold { get; set; } = 68.0m;
        public decimal H4OverheatedRsiThreshold { get; set; } = 74.0m;
        public decimal MinAtrRatioForDeepEntry { get; set; } = 4.0m;
        public decimal NearHighDistanceTo20dHighThreshold { get; set; } = -8.0m;
        public decimal FastContinuationMinDailyMaDistancePct { get; set; } = 0.0m;
        public decimal FastContinuationMinH4MaDistancePct { get; set; } = -3.0m;
        public decimal LaunchContinuationMinDailyMaDistancePct { get; set; } = -15.0m;
        public decimal LaunchContinuationMinH4MaDistancePct { get; set; } = -3.0m;
        public decimal LaunchContinuationMinDailyMaSlopePct { get; set; } = 20.0m;
        public decimal LaunchContinuationMinH4Rsi { get; set; } = 60.0m;
        public decimal LaunchContinuationMinH4Macd { get; set; } = 0.10m;
        public decimal ModeratePullbackMinDailyMaDistancePct { get; set; } = -8.0m;
        public decimal ModeratePullbackMinH4MaDistancePct { get; set; } = -8.0m;
        public decimal DeepPullbackMaxMaDistancePct { get; set; } = -8.0m;
        public decimal LateSpikeDailyRsiThreshold { get; set; } = 78.0m;
        public decimal LateSpikeH4RsiThreshold { get; set; } = 74.0m;
        public decimal LateSpikeDailyMaSlopeThreshold { get; set; } = 25.0m;
        public decimal LateSpikeH4MaSlopeThreshold { get; set; } = 15.0m;
        public decimal MaxDiscountPct { get; set; } = 0.10m;
    }

    public class H4BollingerEntrySettings
    {
        public H4TriangleEntrySettings Triangle { get; set; } = new();
    }

    public class H4TriangleEntrySettings
    {
        public bool Enabled { get; set; } = true;
        public int ImpulseAndDriftBars { get; set; } = 6;
        public decimal MinImpulseBodyPct { get; set; } = 0.04m;
        public decimal MaxDriftBodyPct { get; set; } = 0.015m;
        public decimal MinDriftCandlesNearEdge { get; set; } = 3m;
        public decimal MinSmallCandleLowPositionPctOfImpulse { get; set; } = 0.55m;
        public decimal EntryBufferBelowShortLowsPct { get; set; } = 0.0025m;
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
        public decimal DefaultProfitPct { get; set; } = 0.09m;
        public decimal MinProfitPct { get; set; } = 0.06m;
        public decimal MaxProfitPct { get; set; } = 0.13m;
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
        public decimal DefaultProfitPct { get; set; } = 0.10m;
        public decimal MinProfitPct { get; set; } = 0.07m;
        public decimal MaxProfitPct { get; set; } = 0.15m;
    }

    public class ExplosiveMinFirstExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinDailyTrendPosition { get; set; } = 4m;
        public decimal MinTrendPosition { get; set; } = 8m;
        public decimal MinAtrRatio { get; set; } = 3m;
        public decimal MinDailyRsi14 { get; set; } = 55m;
        public decimal MaxDistanceTo20dHigh { get; set; } = -3m;
        public decimal DefaultProfitPct { get; set; } = 0.13m;
        public decimal MinProfitPct { get; set; } = 0.09m;
        public decimal MaxProfitPct { get; set; } = 0.20m;
        public decimal EntryDiscountPct { get; set; } = 0m;
        public decimal MaxEntryDiscountPct { get; set; } = 0.012m;
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
        public decimal DefaultProfitPct { get; set; } = 0.09m;
        public decimal MinProfitPct { get; set; } = 0.06m;
        public decimal MaxProfitPct { get; set; } = 0.12m;
    }

    public class WeakDeepPullbackExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MaxDailyTrendPosition { get; set; } = 1m;
        public decimal MinAtrRatio { get; set; } = 3m;
        public decimal EntryDiscountPct { get; set; } = 0.06m;
        public decimal HighAtrEntryDiscountPct { get; set; } = 0.07m;
        public decimal HighAtrRatioThreshold { get; set; } = 4m;
        public decimal DefaultProfitPct { get; set; } = 0.08m;
        public decimal MinProfitPct { get; set; } = 0.05m;
        public decimal MaxProfitPct { get; set; } = 0.10m;
        public decimal MaxLossPct { get; set; } = 0.06m;
    }

    public class ReversalRecoveryExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinAtrRatio { get; set; } = 3.0m;
        public decimal MinH4RsiSlope { get; set; } = 25.0m;
        public decimal MinH4MacdSlope { get; set; } = 0.15m;
        public decimal MaxDailyTrendPosition { get; set; } = 1.0m;
        public decimal MaxTrendPosition { get; set; } = 2.0m;
        public decimal SeriesMinAtrRatio { get; set; } = 2.4m;
        public decimal SeriesMaxDailyRsi14 { get; set; } = 52.0m;
        public decimal SeriesMaxDistanceTo20dHigh { get; set; } = -10.0m;
        public decimal SeriesMaxDailyMidSlopePct { get; set; } = -1.0m;
        public decimal SeriesMinDailyWidthCompressionPct { get; set; } = 1.0m;
        public decimal SeriesMinH4MacdTailSlope { get; set; } = 0.05m;
        public decimal SeriesMinH4WidthExpansionPct { get; set; } = 3.0m;
        public decimal SeriesMinH4RsiTailSlope { get; set; } = 8.0m;
        public decimal SeriesMinH4RsiTailForMacdLeg { get; set; } = -2.0m;
        public decimal SeriesMaxH4RsiTailSlope { get; set; } = 4.0m;
        public decimal EntryDiscountPct { get; set; } = 0.015m;
        public decimal MaxEntryDiscountPct { get; set; } = 0.02m;
        public decimal NearTermEntryDiscountPct { get; set; } = 0.02m;
        public decimal NearTermMaxEntryDiscountPct { get; set; } = 0.025m;
        public decimal NearTermMinAtrRatio { get; set; } = 4.0m;
        public decimal NearTermMaxDistanceTo20dHigh { get; set; } = -15.0m;
        public decimal NearTermMaxDailyRsi14 { get; set; } = 55.0m;
        public decimal NearTermMaxBbMid { get; set; } = -4.0m;
        public decimal NearTermMinDailyMidSlopePct { get; set; } = 2.0m;
        public decimal NearTermMinDailyWidthCompressionPct { get; set; } = 1.0m;
        public decimal NearTermMinH4WidthExpansionPct { get; set; } = 2.0m;
        public decimal NearTermMinH4RsiTailSlope { get; set; } = 6.0m;
        public decimal NearTermMinH4MacdTailSlope { get; set; } = 0.05m;
        public decimal DefaultProfitPct { get; set; } = 0.10m;
        public decimal MinProfitPct { get; set; } = 0.07m;
        public decimal MaxProfitPct { get; set; } = 0.14m;
        public decimal MaxLossPct { get; set; } = 0.05m;
    }

    public class ExplosiveMaxFirstExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal MinDailyRsi14 { get; set; } = 60m;
        public decimal MinAtrRatio { get; set; } = 3.5m;
        public decimal MaxVolumeRatio20 { get; set; } = 0.35m;
        public decimal DefaultProfitPct { get; set; } = 0.10m;
        public decimal MinProfitPct { get; set; } = 0.07m;
        public decimal MaxProfitPct { get; set; } = 0.14m;
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
        public decimal DefaultProfitPct { get; set; } = 0.12m;
        public decimal MinProfitPct { get; set; } = 0.09m;
        public decimal MaxProfitPct { get; set; } = 0.20m;
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
        public decimal DefaultProfitPct { get; set; } = 0.10m;
        public decimal MinProfitPct { get; set; } = 0.07m;
        public decimal MaxProfitPct { get; set; } = 0.16m;
        public decimal EntryDiscountPct { get; set; } = 0.02m;
        public decimal MaxLossPct { get; set; } = 0.06m;
    }

    public class ResearchLikeExitSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal EntryDiscountPct { get; set; } = 0.01m;
        public decimal EarlyEntryDiscountPct { get; set; } = 0.025m;
        public decimal DefaultProfitPct { get; set; } = 0.10m;
        public decimal MinProfitPct { get; set; } = 0.07m;
        public decimal MaxProfitPct { get; set; } = 0.14m;
        public decimal MinDailyRsiSlope { get; set; } = 18m;
        public decimal MinDailyMacdSlope { get; set; } = 0.18m;
        public decimal MinH4RsiSlope { get; set; } = 30m;
        public decimal MinH4MacdSlope { get; set; } = 0.15m;
        public int MinH4RsiUpMoves { get; set; } = 8;
    }

    public class AiReferenceTradePlanSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal EntryDiscountPct { get; set; } = 0.02m;
        public decimal DefaultProfitPct { get; set; } = 0.10m;
        public decimal MinProfitPct { get; set; } = 0.07m;
        public decimal MaxProfitPct { get; set; } = 0.16m;
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
        public decimal ReversalDeepHookBonus { get; set; } = 0.75m;
        public decimal ReversalDeepHookMaxTrendPosition { get; set; } = -8.0m;
        public decimal ReversalDeepHookMaxDailyTrendPosition { get; set; } = -8.0m;
        public decimal ReversalDeepHookMaxDistanceTo20dHigh { get; set; } = -20.0m;
        public decimal ReversalDeepHookMaxBbMid { get; set; } = -3.0m;
        public decimal ReversalDeepHookMinDailyRsi14 { get; set; } = 30.0m;
        public decimal ReversalDeepHookMaxDailyRsi14 { get; set; } = 55.0m;
        public decimal ReversalDeepHookMinAtrRatio { get; set; } = 3.4m;
        public decimal ReversalH4BellUpHybridBonus { get; set; } = 1.0m;
        public decimal ReversalH4BellUpMaxDistanceTo20dHigh { get; set; } = -18.0m;
        public decimal ReversalH4BellUpMinDailyRsi14 { get; set; } = 35.0m;
        public decimal ReversalH4BellUpMaxDailyRsi14 { get; set; } = 55.0m;
        public decimal ReversalH4BellUpMinAtrRatio { get; set; } = 3.0m;
        public decimal ReversalHighAmplitudeBonus { get; set; } = 0.75m;
        public decimal ReversalHighAmplitudeStrongBonus { get; set; } = 0.35m;
        public decimal ReversalHighAmplitudeMinDailyRsi14 { get; set; } = 29.0m;
        public decimal ReversalHighAmplitudeEarlyRsiMax { get; set; } = 43.0m;
        public decimal ReversalHighAmplitudeMaxDailyRsi14 { get; set; } = 52.0m;
        public decimal ReversalHighAmplitudeMinAtrRatio { get; set; } = 2.4m;
        public decimal ReversalHighAmplitudeStrongAtrRatio { get; set; } = 3.8m;
        public decimal ReversalHighAmplitudeMaxDistanceTo20dHigh { get; set; } = -13.0m;
        public decimal ReversalHighAmplitudeMaxPullback10d { get; set; } = -8.0m;
        public decimal ReversalHighAmplitudeMaxDailyPullback10d { get; set; } = -8.0m;
        public decimal ReversalHighAmplitudeMinTrendPosition { get; set; } = -25.0m;
        public decimal ReversalHighAmplitudeMinDailyTrendPosition { get; set; } = -30.0m;
        public decimal ReversalHighAmplitudeMinBbMid { get; set; } = -8.0m;
        public decimal ReversalHighAmplitudeMidRsiMinDailyTrendPosition { get; set; } = -12.0m;
        public decimal ReversalHighAmplitudeMidRsiMinBbMid { get; set; } = -4.0m;
        public decimal ReversalConstructiveDeepBounceBonus { get; set; } = 0.75m;
        public decimal ReversalConstructiveDeepBounceMinCandidateScore { get; set; } = 30.0m;
        public decimal ReversalConstructiveDeepBounceMinEntryScore { get; set; } = 8.0m;
        public decimal ReversalConstructiveDeepBounceStrongEntryScore { get; set; } = 18.0m;
        public decimal ReversalConstructiveDeepBounceStrongEntryMinBbMid { get; set; } = -2.0m;
        public decimal ReversalConstructiveDeepBounceMinDailyRsi14 { get; set; } = 29.0m;
        public decimal ReversalConstructiveDeepBounceMaxDailyRsi14 { get; set; } = 46.0m;
        public decimal ReversalConstructiveDeepBounceMinAtrRatio { get; set; } = 3.2m;
        public decimal ReversalConstructiveDeepBounceMaxDistanceTo20dHigh { get; set; } = -15.0m;
        public decimal ReversalConstructiveDeepBounceMaxPullback10d { get; set; } = -8.0m;
        public decimal ReversalConstructiveDeepBounceMaxDailyPullback10d { get; set; } = -12.0m;
        public decimal ReversalConstructiveDeepBounceMinDailyTrendPosition { get; set; } = -16.0m;
        public decimal ReversalConstructiveDeepBounceMinBbMid { get; set; } = -6.0m;
        public decimal ReversalBrokenDownPenalty { get; set; } = 0.75m;
        public decimal ReversalBrokenDownMaxBbMid { get; set; } = -8.0m;
        public decimal ReversalBrokenDownMaxTrendPosition { get; set; } = -10.0m;
        public decimal ReversalBrokenDownMaxDailyTrendPosition { get; set; } = -10.0m;
        public decimal ReversalBrokenDownMaxDistanceTo20dHigh { get; set; } = -24.0m;
        public decimal ReversalMatureWeakBouncePenalty { get; set; } = 0.55m;
        public decimal ReversalMatureWeakBounceMinDailyRsi14 { get; set; } = 53.0m;
        public decimal ReversalMatureWeakBounceMinDistanceTo20dHigh { get; set; } = -18.0m;
        public decimal ReversalMatureWeakBounceMaxBbMid { get; set; } = 0.0m;
        public decimal ReversalSeriesRecoveryBonus { get; set; } = 0.85m;
        public decimal ReversalWeakContinuationPenalty { get; set; } = 0.65m;
        public decimal ReversalSeriesMinAtrRatio { get; set; } = 2.4m;
        public decimal ReversalSeriesMaxDailyRsi14 { get; set; } = 52.0m;
        public decimal ReversalSeriesMaxDistanceTo20dHigh { get; set; } = -10.0m;
        public decimal ReversalSeriesMaxDailyMidSlopePct { get; set; } = -1.0m;
        public decimal ReversalSeriesMinDailyWidthCompressionPct { get; set; } = 1.0m;
        public decimal ReversalSeriesMinH4MacdTailSlope { get; set; } = 0.05m;
        public decimal ReversalSeriesMinH4WidthExpansionPct { get; set; } = 3.0m;
        public decimal ReversalSeriesMinH4RsiTailSlope { get; set; } = 8.0m;
        public decimal ReversalSeriesMinH4RsiTailForMacdLeg { get; set; } = -2.0m;
        public decimal ReversalWeakMaxDailyRsi14 { get; set; } = 45.0m;
        public decimal ReversalWeakMaxH4RsiTailSlope { get; set; } = -3.0m;
        public decimal ReversalWeakMaxH4MacdTailSlope { get; set; } = 0.0m;
        public decimal ReversalWeakMaxH4MidSlopePct { get; set; } = 1.0m;

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
        public decimal LateContinuationPenalty { get; set; } = 0.20m;
        public decimal ResearchLikeDistanceTo20dHighThreshold { get; set; } = -10.0m;
        public decimal ResearchLikeMaxDailyRsi14 { get; set; } = 58.0m;
        public decimal ResearchLikeMinAtrRatio { get; set; } = 2.2m;
        public decimal ResearchLikeMaxTrendPosition { get; set; } = 1.5m;
        public decimal ResearchLikeMaxBbMid { get; set; } = 1.0m;
        public decimal ResearchLikeBonus { get; set; } = 0.24m;
        public decimal ResearchLikeStrongPatternBonus { get; set; } = 0.16m;
        public decimal ResearchLikeDailyMacdSlopeThreshold { get; set; } = 0.08m;
        public decimal ResearchLikeExtraBonus { get; set; } = 0.12m;
        public decimal BellUpPhaseNotReadyPenalty { get; set; } = 1.0m;
        public decimal OverextendedTrendPositionThreshold { get; set; } = 8.0m;
        public decimal OverextendedDailyRsi14Threshold { get; set; } = 66.0m;
        public decimal OverextendedBbMidThreshold { get; set; } = 5.0m;
        public decimal OverextendedPenalty { get; set; } = 0.16m;
        public decimal ExhaustedMoverDailyRsi14Threshold { get; set; } = 80.0m;
        public decimal ExhaustedMoverTrendPositionThreshold { get; set; } = 9.0m;
        public decimal ExhaustedMoverDailyTrendPositionThreshold { get; set; } = 10.0m;
        public decimal ExhaustedMoverPenalty { get; set; } = 3.0m;
        public decimal HighAmplitudeProxyMinAtrRatio { get; set; } = 4.0m;
        public decimal HighAmplitudeProxyDeepDistanceTo20dHigh { get; set; } = -12.0m;
        public decimal HighAmplitudeProxyDeepPullback10d { get; set; } = -8.0m;
        public decimal HighAmplitudeProxyConstructiveRsiMin { get; set; } = 35.0m;
        public decimal HighAmplitudeProxyConstructiveRsiMax { get; set; } = 68.0m;
        public decimal HighAmplitudeProxyBonus { get; set; } = 0.65m;
        public decimal HighAmplitudeProxyStrongAtrRatio { get; set; } = 5.5m;
        public decimal HighAmplitudeProxyStrongBonus { get; set; } = 0.35m;
        public decimal RunawayHighAmplitudeTrendPositionThreshold { get; set; } = 8.0m;
        public decimal RunawayHighAmplitudeTrendBonus { get; set; } = 0.35m;
        public decimal LowAmplitudeProxyMaxAtrRatio { get; set; } = 3.8m;
        public decimal LowAmplitudeProxyShallowPullback10d { get; set; } = -7.0m;
        public decimal LowAmplitudeProxyPenalty { get; set; } = 1.05m;
        public decimal AnomalousVolatilityAtrRatioThreshold { get; set; } = 15.0m;
        public decimal AnomalousVolatilityMaxVolumeRatio20 { get; set; } = 0.10m;
        public decimal AnomalousVolatilityPenalty { get; set; } = 1.5m;
        public decimal ExplosiveBellUpAnomalyBypassBonus { get; set; } = 1.8m;
        public decimal ExplosiveBellUpAnomalyMinAtrRatio { get; set; } = 15.0m;
        public decimal ExplosiveBellUpAnomalyMinTrendPosition { get; set; } = 30.0m;
        public decimal ExplosiveBellUpAnomalyMaxDailyRsi14 { get; set; } = 75.0m;
        public decimal ExplosiveBellUpAnomalyMaxDistanceTo20dHigh { get; set; } = -15.0m;

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
        public int MaxItems { get; set; } = 10;
        public decimal MinEntryScore { get; set; } = 15m;
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
        public bool EmitAllSeenCandidates { get; set; } = false;
    }

    public class PreFilterSettings
    {
        public string RequiredCurrency { get; set; } = "USD";
        public List<string> DenyList { get; set; } = [];
        public List<string> AllowedStockTypeMarkers { get; set; } = [];
        public List<string> RejectTickersEndingWith { get; set; } = [];
        public string TickerSuffixExceptionFile { get; set; } = "Tickers/ticker-suffix-exceptions.json";
        public int RecentDailyPriceFloorDays { get; set; } = 3;
        public decimal MinRecentDailyClosePrice { get; set; } = 5m;
        public decimal MinRecentDailyLowPrice { get; set; } = 4m;
        public decimal RecentDailyCloseFloorTolerance { get; set; } = 0.25m;
        public decimal RecoveredCloseFloorBuffer { get; set; } = 1.0m;
    }
}
