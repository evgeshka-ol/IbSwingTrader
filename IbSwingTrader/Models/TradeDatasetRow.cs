namespace IbSwingTrader.Models
{
    public class TradeDatasetRow
    {
        // базовая информация
        public required string Ticker { get; set; }

        public DateTime EntryDate { get; set; }

        public double EntryPrice { get; set; }

        // реальная сделка или синтетическая
        public bool IsRealTrade { get; set; }

        // данные сделки (если реальная)
        public double? ExitPrice { get; set; }

        public int? HoldDays { get; set; }

        public double? ProfitPct { get; set; }

        // признаки рынка в момент входа
        public double Pullback5d { get; set; }

        public double Pullback10d { get; set; }

        public double BBPosition { get; set; }

        public double VolumeRatio20 { get; set; }

        public double RSI14 { get; set; }

        public double ATRRatio { get; set; }

        public double MACDHist { get; set; }

        public double TrendPosition { get; set; }

        // будущие движения
        public double FutureHigh1d { get; set; }

        public double FutureHigh2d { get; set; }

        public double FutureLow1d { get; set; }

        public double FutureLow2d { get; set; }

        // цели стратегии
        public bool Target10pct1d { get; set; }

        public bool Target10pct2d { get; set; }
    }
}
