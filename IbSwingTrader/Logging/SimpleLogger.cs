using System.Text;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Logging
{

    public class SimpleLogger : ILogger
    {
        private readonly StreamWriter _file;

        public SimpleLogger(string path)
        {
            _file = new StreamWriter(path, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        public void EmptyLine()
        {
            Console.WriteLine();
            _file.WriteLine();
        }

        public void Info(string message)
        {
            Console.WriteLine(message);
            WriteFile("INFO", message);
        }

        public void Debug(string message)
        {
            WriteFile("DEBUG", message);
        }

        public void Error(string message)
        {
            Console.WriteLine(message);
            WriteFile("ERROR", message);
        }

        private void WriteFile(string level, string message)
        {
            _file.WriteLine($"{DateTime.UtcNow:HH:mm:ss} [{level}] {message}");
        }
    }
}
