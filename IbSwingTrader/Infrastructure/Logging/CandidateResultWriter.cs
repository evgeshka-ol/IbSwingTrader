using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateResultWriter() : ICandidateResultWriter
    {
        private readonly string _file = $"candidates/candidates_{DateTime.UtcNow:yyyyMMdd_HHmm}.json";
        private static readonly HashSet<string> RoundTo2Fields =
            [
                nameof(Candidate.EntryPrice),
                nameof(Candidate.ExitPrice),
                nameof(Candidate.StopLoss),
                nameof(Candidate.ProfitPercent),
                nameof(Candidate.LossPercent)
            ];

        public async Task WriteAsync(List<CandidateDetails> candidates)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);

            foreach (var c in candidates)
            {
                WriteCandidate(c);
            }

            var json = BuildJson(candidates);

            await File.WriteAllTextAsync(_file, json);
        }

        private static string BuildJson(IEnumerable<CandidateDetails> candidates)
        {
            var array = new JsonArray();

            foreach (var candidate in candidates)
            {
                array.Add(ToJsonObject(candidate));
            }

            return array.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private static JsonObject ToJsonObject<T>(T item)
        {
            var type = typeof(T);

            var baseProps = type.BaseType?
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(p => p.MetadataToken)
                ?? Enumerable.Empty<PropertyInfo>();

            var ownProps = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.MetadataToken);

            var props = baseProps
                .Concat(ownProps)
                .ToArray();

            var obj = new JsonObject();

            foreach (var prop in props)
            {
                var value = prop.GetValue(item);
                obj[prop.Name] = ToJsonNode(value, prop.Name);
            }

            return obj;
        }

        private static JsonNode? ToJsonNode(object? value, string propertyName)
        {
            if (value == null)
                return null;

            return value switch
            {
                decimal d when RoundTo2Fields.Contains(propertyName)
                    => JsonValue.Create(Math.Round(d, 2, MidpointRounding.AwayFromZero)),

                decimal d
                    => JsonValue.Create(decimal.Parse(
                        d.ToString("F6", CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture)),

                DateTime dt => JsonValue.Create(
                    propertyName.EndsWith("Date", StringComparison.OrdinalIgnoreCase)
                        ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),

                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                long l => JsonValue.Create(l),
                double d => JsonValue.Create(d),
                float f => JsonValue.Create(f),

                _ => JsonValue.Create(value.ToString())
            };
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
