namespace IbSwingTrader.Domain.Dataset
{
    public class FeatureSet
    {
        public decimal DistanceTo20dHigh { get; set; }
        public decimal DistanceTo52wHigh { get; set; }

        public decimal H4MaSignedDistancePct { get; set; }
        public decimal H4BollingerUpperDistancePct { get; set; }
        public decimal H4BollingerBandWidthPct { get; set; }
        public decimal RSI14 { get; set; }
        public decimal MACDLineMinusSignal { get; set; }

        public decimal DailyMaSignedDistancePct { get; set; }
        public decimal DailyBollingerUpperDistancePct { get; set; }
        public decimal DailyBollingerBandWidthPct { get; set; }
        public decimal DailyRSI14 { get; set; }
        public decimal DailyMACDLineMinusSignal { get; set; }

        public decimal? WeeklyMaSignedDistancePct { get; set; }
        public decimal? WeeklyBollingerUpperDistancePct { get; set; }
        public decimal? WeeklyBollingerBandWidthPct { get; set; }
        public decimal? WeeklyRSI14 { get; set; }
        public decimal? WeeklyMACDLineMinusSignal { get; set; }
    }
}
