namespace IbSwingTrader.Domain.Dataset
{
    public class ResearchDatasetRow : TickerEntity
    {
        public required string Mode { get; set; }

        public required string Source { get; set; }

        public required string ReferenceType { get; set; }

        public DateTime ReferenceTime { get; set; }

        public decimal ReferencePrice { get; set; }

        public DateTime PeakTime { get; set; }

        public decimal PeakPrice { get; set; }

        public decimal RunupPct { get; set; }

        public int BarsToPeak { get; set; }

        public decimal MaxDrawdownBeforePeakPct { get; set; }

        public decimal DistanceTo20dHigh { get; set; }

        public decimal DistanceTo52wHigh { get; set; }

        public decimal H4MaSignedDistancePct { get; set; }

        public decimal H4Rsi14 { get; set; }

        public decimal H4MacdLineMinusSignal { get; set; }

        public decimal DailyMaSignedDistancePct { get; set; }

        public decimal DailyRsi14 { get; set; }

        public decimal DailyMacdLineMinusSignal { get; set; }

        public decimal? WeeklyMaSignedDistancePct { get; set; }

        public decimal? WeeklyRsi14 { get; set; }

        public decimal? WeeklyMacdLineMinusSignal { get; set; }

        public decimal DailyBollingerUpperDistancePct { get; set; }

        public decimal DailyBollingerBandWidthPct { get; set; }

        public decimal? WeeklyBollingerUpperDistancePct { get; set; }

        public decimal? WeeklyBollingerBandWidthPct { get; set; }

        public decimal Pullback10d { get; set; }

        public decimal DailyPullback10d { get; set; }

        public decimal VolumeRatio20 { get; set; }

        public decimal AtrRatio { get; set; }

        public decimal TrendPosition { get; set; }

        public decimal DailyTrendPosition { get; set; }

        public decimal BbMidSignedDistancePct { get; set; }

        public decimal? WeeklyMacdHistDelta { get; set; }

        public List<decimal> DailyMaDistances { get; set; } = [];
        public List<decimal> DailyBollingerUpperDistances { get; set; } = [];
        public List<decimal> DailyBollingerBandWidths { get; set; } = [];
        public List<decimal> DailyRsiValues { get; set; } = [];
        public List<decimal> DailyMacdValues { get; set; } = [];

        public List<decimal> WeeklyMaDistances { get; set; } = [];
        public List<decimal> WeeklyBollingerUpperDistances { get; set; } = [];
        public List<decimal> WeeklyBollingerBandWidths { get; set; } = [];
        public List<decimal> WeeklyRsiValues { get; set; } = [];
        public List<decimal> WeeklyMacdValues { get; set; } = [];

        public List<decimal>? H4MaDistances { get; set; }
        public List<decimal>? H4BollingerUpperDistances { get; set; }
        public List<decimal>? H4BollingerBandWidths { get; set; }
        public List<decimal>? H4RsiValues { get; set; }
        public List<decimal>? H4MacdValues { get; set; }
    }
}
