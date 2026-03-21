namespace IbSwingTrader.Models
{
    public class IbkrSettings
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 7497;
        public int ClientId { get; set; } = 7;
        public bool UseRth { get; set; }
        public int HistoryTimeoutSeconds { get; set; } = 60;
        public int ReconnectDelaySeconds { get; set; } = 5;
        public int MaxParallelHistoryRequests { get; set; } = 2;
        public int HistoryRequestPauseMs { get; set; } = 300;
    }
}