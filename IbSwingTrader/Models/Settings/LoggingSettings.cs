namespace IbSwingTrader.Models.Settings
{
    public class LoggingSettings
    {
        public LogLevel FileMinimumLevel { get; set; } = LogLevel.Debug;

        public LogLevel ConsoleMinimumLevel { get; set; } = LogLevel.Info;

        public bool EnableColors { get; set; } = true;
    }
}