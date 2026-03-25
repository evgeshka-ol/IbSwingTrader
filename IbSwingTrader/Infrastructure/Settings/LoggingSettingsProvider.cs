using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class LoggingSettingsProvider(IAgentSettingsProvider settingsProvider) : ILoggingSettingsProvider
    {
        private readonly LoggingSettings _logging = settingsProvider.Get().Logging ?? new LoggingSettings();

        public LoggingSettings Get() => _logging;
    }
}