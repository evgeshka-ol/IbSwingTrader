namespace IbSwingTrader.Models
{
    public class LoggingSettings
    {
        public bool EnableConsoleColors { get; set; } = true;
        public string MinimumLevel { get; set; } = "Information";
        public bool WriteToFile { get; set; } = true;
    }
}