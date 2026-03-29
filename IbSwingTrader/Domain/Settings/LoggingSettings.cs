namespace IbSwingTrader.Domain.Settings
{
    public class LoggingSettings
    {
        public bool EnableFileLogging { get; set; } = true;

        public LogLevel FileMinimumLevel { get; set; } = LogLevel.Debug;

        public LogLevel ConsoleMinimumLevel { get; set; } = LogLevel.Info;

        public bool EnableColors { get; set; } = true;
    }
}
