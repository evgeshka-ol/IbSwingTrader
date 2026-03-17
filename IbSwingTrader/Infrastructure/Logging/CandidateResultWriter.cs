using System.Text.Json;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateResultWriter() : ICandidateResultWriter
    {
        private readonly string _file = $"candidates_{DateTime.UtcNow:yyyyMMdd_HHmm}.json";
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public async Task WriteAsync(List<CandidateDetails> candidates)
        {
            foreach (var c in candidates)
            {
                WriteCandidate(c);
            }

            var json = JsonSerializer.Serialize(candidates, _jsonOptions);

            await File.WriteAllTextAsync(_file, json);
        }

        private static void WriteCandidate(Candidate c)
        {
            Write(ConsoleColor.Gray, $"{c.Ticker} ");

            Write(ConsoleColor.DarkYellow, $"{c.EntryPrice:F2} ");

            Write(ConsoleColor.DarkGreen, $"{c.ExitPrice:F2} ");

            Write(ConsoleColor.DarkRed, $"{c.StopLoss:F2} ");

            Write(ConsoleColor.Green, $"{c.ProfitPercent:+0.00;-0.00}%");

            Write(ConsoleColor.DarkGray, "/");

            Write(ConsoleColor.Red, $"{c.LossPercent:+0.00;-0.00}%");

            Console.WriteLine();
        }

        private static void Write(ConsoleColor color, string text)
        {
            var old = Console.ForegroundColor;

            Console.ForegroundColor = color;
            Console.Write(text);

            Console.ForegroundColor = old;
        }
    }
}
