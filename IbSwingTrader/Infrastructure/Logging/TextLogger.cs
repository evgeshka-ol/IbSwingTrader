using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class TextLogger : ITextLogger
    {
        private static readonly string _filePath = $"logs/log-{DateTime.UtcNow:yyyyMMdd_HHmm}.log";
        private static readonly Lock _lock = new();

        public TextLogger()
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

        public void InfoBlock(string title, string block)
            => WriteBlock("INFO", title, block, ConsoleColor.DarkGray);

        public void ErrorBlock(string title, string block)
            => WriteBlock("ERROR", title, block, ConsoleColor.Red);

        private static void Write(string level, string message, ConsoleColor? color = null)
        {
            var time = DateTime.UtcNow.ToString("HH:mm:ss");
            var line = $"{time} [{level}] | {message}";

            lock (_lock)
            {
                if (level != "DEBUG")
                {
                    var oldColor = Console.ForegroundColor;

                    if (color.HasValue)
                        Console.ForegroundColor = color.Value;

                    Console.WriteLine(line);
                    Console.ForegroundColor = oldColor;

                    File.AppendAllText(_filePath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
                }
            }
        }

        private static void WriteBlock(
            string level,
            string title,
            string block,
            ConsoleColor? color = null)
        {
            var time = DateTime.UtcNow.ToString("HH:mm:ss");
            var header = $"{time} [{level}] | {title}";

            lock (_lock)
            {
                var oldColor = Console.ForegroundColor;

                if (color.HasValue)
                    Console.ForegroundColor = color.Value;

                Console.WriteLine(header);
                Console.WriteLine(block);
                Console.ForegroundColor = oldColor;

                File.AppendAllText(
                    _filePath,
                    $"{DateTime.UtcNow:O} {header}{Environment.NewLine}{block}{Environment.NewLine}");
            }
        }
    }
}
