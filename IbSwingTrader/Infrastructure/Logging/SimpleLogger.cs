using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class SimpleLogger : ILogger
    {
        private static readonly string _filePath = $"logs/log-{DateTime.UtcNow:yyyyMMdd_HHmm}.log";
        private static readonly Lock _lock = new();

        public SimpleLogger()
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public void EmptyLine()
        {
            lock (_lock)
            {
                Console.WriteLine();
                File.AppendAllText(_filePath, Environment.NewLine);
            }
        }

        public void Info(string message)
            => Write("INFO", message, ConsoleColor.DarkGray);

        public void Debug(string message)
            => Write("DEBUG", message);

        public void Error(string message)
            => Write("ERROR", message, ConsoleColor.Red);

        private static void Write(string level, string message, ConsoleColor? color = null)
        {
            var time = DateTime.UtcNow.ToString("HH:mm:ss");
            var line = $"{time} [{level}] | {message}";

            lock (_lock)
            {
                if (color.HasValue)
                {
                    var oldColor = Console.ForegroundColor;
                    Console.ForegroundColor = color.Value;
                    Console.WriteLine(line);
                    Console.ForegroundColor = oldColor;
                }

                File.AppendAllText(_filePath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
            }
        }
    }
}
