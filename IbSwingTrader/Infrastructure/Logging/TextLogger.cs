using System.Text;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class TextLogger : ITextLogger
    {
        private readonly string _logFilePath;
        private readonly IConsoleColorWriter _console;
        private readonly object _fileLock = new();

        public TextLogger(
            IAgentPathService pathService,
            IConsoleColorWriter console)
        {
            _console = console;

            var logsFolder = pathService.GetLogsFolder();
            Directory.CreateDirectory(logsFolder);

            _logFilePath = Path.Combine(
                logsFolder,
                $"log-{DateTime.UtcNow:yyyyMMdd_HHmm}.log");
        }

        public void EmptyLine()
        {
            _console.WriteLine();
            AppendRawLine(string.Empty);
        }

        public void Info(string message)
        {
            Write("INFO", message, ConsoleColor.DarkGray, writeToConsole: true);
        }

        public void Debug(string message)
        {
            Write("DEBUG", message, color: null, writeToConsole: false);
        }

        public void Warning(string message)
        {
            Write("WARNING", message, ConsoleColor.Yellow, writeToConsole: true);
        }

        public void Error(string message)
        {
            Write("ERROR", message, ConsoleColor.Red, writeToConsole: true);
        }

        public void InfoBlock(string title, string block)
        {
            WriteBlock("INFO", title, block, ConsoleColor.DarkGray);
        }

        public void ErrorBlock(string title, string block)
        {
            WriteBlock("ERROR", title, block, ConsoleColor.Red);
        }

        private void Write(
            string level,
            string message,
            ConsoleColor? color,
            bool writeToConsole)
        {
            var line = BuildConsoleLine(level, message);
            var fileLine = BuildFileLine(line);

            if (writeToConsole)
                _console.WriteLine(line, color);

            AppendRawLine(fileLine);
        }

        private void WriteBlock(
            string level,
            string title,
            string block,
            ConsoleColor? color)
        {
            var header = BuildConsoleLine(level, title);
            var fileHeader = BuildFileLine(header);

            _console.WriteLine(header, color);

            if (!string.IsNullOrEmpty(block))
                _console.WriteLine(block, color);

            AppendRawLine(fileHeader);

            if (!string.IsNullOrEmpty(block))
                AppendRawLine(block);
        }

        private static string BuildConsoleLine(string level, string message)
        {
            var time = DateTime.UtcNow.ToString("HH:mm:ss");
            return $"{time} [{level}] | {message}";
        }

        private static string BuildFileLine(string consoleLine)
        {
            return $"{DateTime.UtcNow:O} {consoleLine}";
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