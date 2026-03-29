using System.Text;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class TextLogger : ITextLogger
    {
        private readonly string _logFilePath;
        private readonly IConsoleColorWriter _console;
        private readonly LoggingSettings _settings;
        private readonly Lock _fileLock = new();

        public TextLogger(
            IAgentPathService pathService,
            IConsoleColorWriter console,
            ILoggingSettingsProvider loggingSettingsProvider)
        {
            _console = console;
            _settings = loggingSettingsProvider.Get();

            if (_settings.EnableFileLogging)
            {
                var logsFolder = pathService.GetLogsFolder();
                Directory.CreateDirectory(logsFolder);

                _logFilePath = Path.Combine(
                    logsFolder,
                    $"log-{MarketTime.Now():yyyyMMdd_HHmm}.log");
            }
            else
            {
                _logFilePath = string.Empty;
            }
        }

        public void EmptyLine()
        {
            if (ShouldWriteToConsole(LogLevel.Info))
                _console.WriteLine();

            if (ShouldWriteToFile(LogLevel.Info))
                AppendRawLine(string.Empty);
        }

        public void Info(string message)
        {
            Write(LogLevel.Info, "INFO", message, ConsoleColor.DarkGray);
        }

        public void Debug(string message)
        {
            Write(LogLevel.Debug, "DEBUG", message, null);
        }

        public void Warning(string message)
        {
            Write(LogLevel.Warning, "WARNING", message, ConsoleColor.Yellow);
        }

        public void Error(string message)
        {
            Write(LogLevel.Error, "ERROR", message, ConsoleColor.Red);
        }

        public void InfoBlock(string title, string block)
        {
            WriteBlock(LogLevel.Info, "INFO", title, block, ConsoleColor.DarkGray);
        }

        public void ErrorBlock(string title, string block)
        {
            WriteBlock(LogLevel.Error, "ERROR", title, block, ConsoleColor.Red);
        }

        private void Write(
            LogLevel level,
            string levelName,
            string message,
            ConsoleColor? color)
        {
            var consoleLine = BuildConsoleLine(levelName, message);
            var fileLine = BuildFileLine(consoleLine);

            if (ShouldWriteToConsole(level))
                _console.WriteLine(consoleLine, GetConsoleColor(color));

            if (ShouldWriteToFile(level))
                AppendRawLine(fileLine);
        }

        private void WriteBlock(
            LogLevel level,
            string levelName,
            string title,
            string block,
            ConsoleColor? color)
        {
            var header = BuildConsoleLine(levelName, title);
            var fileHeader = BuildFileLine(header);

            if (ShouldWriteToConsole(level))
            {
                _console.WriteLine(header, GetConsoleColor(color));

                if (!string.IsNullOrEmpty(block))
                    _console.WriteLine(block, GetConsoleColor(color));
            }

            if (ShouldWriteToFile(level))
            {
                AppendRawLine(fileHeader);

                if (!string.IsNullOrEmpty(block))
                    AppendRawLine(block);
            }
        }

        private bool ShouldWriteToConsole(LogLevel level)
        {
            return _settings.ConsoleMinimumLevel != LogLevel.None
                && level >= _settings.ConsoleMinimumLevel;
        }

        private bool ShouldWriteToFile(LogLevel level)
        {
            return _settings.EnableFileLogging
                && _settings.FileMinimumLevel != LogLevel.None
                && level >= _settings.FileMinimumLevel;
        }

        private ConsoleColor? GetConsoleColor(ConsoleColor? color)
        {
            if (!_settings.EnableColors)
                return null;

            return color;
        }

        private static string BuildConsoleLine(string level, string message)
        {
            var time = MarketTime.Now().ToString("HH:mm:ss");
            return $"{time} [{level}] | {message}";
        }

        private static string BuildFileLine(string consoleLine)
        {
            return $"{MarketTime.Now():yyyy-MM-ddTHH:mm:ss.fffffff} {consoleLine}";
        }

        private void AppendRawLine(string line)
        {
            lock (_fileLock)
            {
                File.AppendAllText(
                    _logFilePath,
                    line + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
    }
}
