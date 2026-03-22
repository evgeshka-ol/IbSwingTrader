using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class ConsoleColorWriter : IConsoleColorWriter
    {
        private static readonly Lock _lock = new();

        public void Write(string text, ConsoleColor? color = null)
        {
            lock (_lock)
            {
                var oldColor = Console.ForegroundColor;

                if (color.HasValue)
                    Console.ForegroundColor = color.Value;

                Console.Write(text);
                Console.ForegroundColor = oldColor;
            }
        }

        public void WriteLine(string text = "", ConsoleColor? color = null)
        {
            lock (_lock)
            {
                var oldColor = Console.ForegroundColor;

                if (color.HasValue)
                    Console.ForegroundColor = color.Value;

                Console.WriteLine(text);
                Console.ForegroundColor = oldColor;
            }
        }
    }
}
