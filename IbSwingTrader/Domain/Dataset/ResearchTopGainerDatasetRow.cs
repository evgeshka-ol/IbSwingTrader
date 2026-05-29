namespace IbSwingTrader.Domain.Dataset
{
    public class ResearchTopGainerDatasetRow : TickerEntity
    {
        public DateTime ScanTime { get; set; }

        public decimal ScanPrice { get; set; }

        public DateTime MaxTime { get; set; }

        public decimal MaxPrice { get; set; }

        public decimal AmplitudePct { get; set; }

        public decimal PositivePotentialPct { get; set; }

        public decimal NegativePotentialPct { get; set; }

        public int BarsToMax { get; set; }

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

        public List<decimal> DailyMaSeries { get; set; } = [];
        public List<decimal> DailyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> DailyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> DailyBbWidthSeries { get; set; } = [];
        public List<decimal> DailyBbUpperBandSeries { get; set; } = [];
        public List<decimal> DailyBbMidBandSeries { get; set; } = [];
        public List<decimal> DailyBbLowerBandSeries { get; set; } = [];
        public List<decimal> DailyRsiSeries { get; set; } = [];
        public List<decimal> DailyMacdLineSeries { get; set; } = [];
        public List<decimal> DailyMacdSignalSeries { get; set; } = [];
        public List<decimal> DailyMacdHistogramSeries { get; set; } = [];
        public List<decimal> DailyMacdSeries { get; set; } = [];

        public List<decimal> WeeklyMaSeries { get; set; } = [];
        public List<decimal> WeeklyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> WeeklyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> WeeklyBbWidthSeries { get; set; } = [];
        public List<decimal> WeeklyBbUpperBandSeries { get; set; } = [];
        public List<decimal> WeeklyBbMidBandSeries { get; set; } = [];
        public List<decimal> WeeklyBbLowerBandSeries { get; set; } = [];
        public List<decimal> WeeklyRsiSeries { get; set; } = [];
        public List<decimal> WeeklyMacdLineSeries { get; set; } = [];
        public List<decimal> WeeklyMacdSignalSeries { get; set; } = [];
        public List<decimal> WeeklyMacdHistogramSeries { get; set; } = [];
        public List<decimal> WeeklyMacdSeries { get; set; } = [];

        public List<decimal>? H4MaSeries { get; set; }
        public List<decimal>? H4BbMidDistanceSeries { get; set; }
        public List<decimal>? H4BbUpperDistanceSeries { get; set; }
        public List<decimal>? H4BbWidthSeries { get; set; }
        public List<decimal>? H4BbUpperBandSeries { get; set; }
        public List<decimal>? H4BbMidBandSeries { get; set; }
        public List<decimal>? H4BbLowerBandSeries { get; set; }
        public List<decimal>? H4RsiSeries { get; set; }
        public List<decimal>? H4MacdLineSeries { get; set; }
        public List<decimal>? H4MacdSignalSeries { get; set; }
        public List<decimal>? H4MacdHistogramSeries { get; set; }
        public List<decimal>? H4MacdSeries { get; set; }
    }
}
