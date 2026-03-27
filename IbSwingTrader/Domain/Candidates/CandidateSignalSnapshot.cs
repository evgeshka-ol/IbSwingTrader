namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateSignalSnapshot
    {
        public required FeatureSet Current { get; init; }
        public required FeatureSet Prev1 { get; init; }
        public required FeatureSet Prev2 { get; init; }
        public required FeatureSet Prev3 { get; init; }

        public decimal DailyMaDelta1 => Current.DailyMaSignedDistancePct - Prev1.DailyMaSignedDistancePct;
        public decimal DailyMaDelta3 => Current.DailyMaSignedDistancePct - Prev3.DailyMaSignedDistancePct;

        public decimal DailyRsiDelta1 => Current.DailyRSI14 - Prev1.DailyRSI14;
        public decimal DailyRsiDelta3 => Current.DailyRSI14 - Prev3.DailyRSI14;

        public decimal DailyMacdDelta1 => Current.DailyMACDLineMinusSignal - Prev1.DailyMACDLineMinusSignal;
        public decimal DailyMacdDelta3 => Current.DailyMACDLineMinusSignal - Prev3.DailyMACDLineMinusSignal;

        public decimal H4MaDelta1 => Current.H4MaSignedDistancePct - Prev1.H4MaSignedDistancePct;
        public decimal H4MaDelta3 => Current.H4MaSignedDistancePct - Prev3.H4MaSignedDistancePct;

        public decimal H4RsiDelta1 => Current.RSI14 - Prev1.RSI14;
        public decimal H4RsiDelta3 => Current.RSI14 - Prev3.RSI14;

        public decimal H4MacdDelta1 => Current.MACDLineMinusSignal - Prev1.MACDLineMinusSignal;
        public decimal H4MacdDelta3 => Current.MACDLineMinusSignal - Prev3.MACDLineMinusSignal;
    }
}