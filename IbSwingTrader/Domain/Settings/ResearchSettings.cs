namespace IbSwingTrader.Domain.Settings
{
    public class ResearchSettings
    {
        public string Mode { get; set; } = "top_gainers";
        public string Source { get; set; } = "known_tickers";
        public string OutputFile { get; set; } = "datasets/research_top_gainers.csv";
        public int LookbackCalendarDays { get; set; } = 240;
        public int MinimumCandles { get; set; } = 120;
        public int MaxParallelTickers { get; set; } = 2;
        public int LocalExtremaLookbackBars { get; set; } = 3;
        public int MaxBarsToPeak { get; set; } = 30;
        public decimal MinRunupPct { get; set; } = 20m;
    }
}
