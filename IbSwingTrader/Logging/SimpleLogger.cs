using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Logging
{

    public class SimpleLogger : ILogger
    {
        private static readonly string _filePath = $"logs/log-{DateTime.UtcNow:yyyyMMdd}.log";
        private static readonly Lock _lock = new();

        public SimpleLogger()
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public void EmptyLine()
            => Write("EMPTY-LINE");

        public void Info(string message)
            => Write("INFO", message, ConsoleColor.DarkGray);

        public void Debug(string message)
            => Write("DEBUG", message);

        public void Error(string message)
            => Write("ERROR", message, ConsoleColor.Red);

        private static void Write(string level, string? message = null, ConsoleColor? color = null)
        {
            var time = DateTime.UtcNow.ToString("HH:mm:ss");
            var line = $"{time} [{level}] | {message}";

            lock (_lock)
            {
                var oldColor = Console.ForegroundColor;

                if (color != null)
                {
                    Console.ForegroundColor = color.Value;
                    Console.WriteLine(line);
                    Console.ForegroundColor = oldColor;
                }

                if (message == null && level == "EMPTY-LINE")
                    File.AppendText(_filePath).WriteLine();
                else
                    File.AppendAllText(_filePath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
            }
        }
    }
}
