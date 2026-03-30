namespace IbSwingTrader.Domain.Dataset
{
    public class ResearchDatasetRow : TickerEntity
    {
        public required string Mode { get; set; }

        public required string Source { get; set; }

        public required string ReferenceType { get; set; }

        public DateTime ReferenceTimeMarket { get; set; }

        public decimal ReferencePrice { get; set; }

        public DateTime PeakTimeMarket { get; set; }

        public decimal PeakPrice { get; set; }

        public decimal RunupPct { get; set; }

        public int BarsToPeak { get; set; }

        public decimal MaxDrawdownBeforePeakPct { get; set; }

        public decimal DistanceTo20dHigh { get; set; }

        public decimal DistanceTo52wHigh { get; set; }

        public List<decimal> DailyMaDistances { get; set; } = [];
        public List<decimal> DailyRsiValues { get; set; } = [];
        public List<decimal> DailyMacdValues { get; set; } = [];

        public List<decimal> WeeklyMaDistances { get; set; } = [];
        public List<decimal> WeeklyRsiValues { get; set; } = [];
        public List<decimal> WeeklyMacdValues { get; set; } = [];

        public List<decimal>? H4MaDistances { get; set; }
        public List<decimal>? H4RsiValues { get; set; }
        public List<decimal>? H4MacdValues { get; set; }
    }
}
