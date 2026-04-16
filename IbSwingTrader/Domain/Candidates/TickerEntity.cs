namespace IbSwingTrader.Domain.Candidates
{
    public abstract class TickerEntity
    {
        [System.Text.Json.Serialization.JsonPropertyOrder(-1000)]
        public required string Ticker { get; set; }
    }
}
