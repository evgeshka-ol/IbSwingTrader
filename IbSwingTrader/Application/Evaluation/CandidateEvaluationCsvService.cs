using System.Globalization;
using System.Reflection;
using System.Text;

namespace IbSwingTrader.Application.Evaluation
{
    public class CandidateEvaluationCsvService : ICandidateEvaluationCsvService
    {
        public async Task WriteAsync(string path, List<CandidateEvaluationResult> results)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            await WriteRecordsAsync(path, results);
        }

        private static async Task WriteRecordsAsync<T>(string path, List<T> records)
        {
            var properties = typeof(T)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanRead)
                .OrderBy(x => x.MetadataToken)
                .ToArray();

            var sb = new StringBuilder();

            sb.AppendLine(string.Join(";", properties.Select(x => Escape(x.Name))));

            foreach (var record in records)
            {
                var values = properties
                    .Select(x => FormatValue(x.GetValue(record)))
                    .Select(Escape);

                sb.AppendLine(string.Join(";", values));
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            var type = value.GetType();

            if (type == typeof(DateTime))
                return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);

            if (type == typeof(DateTimeOffset))
                return ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);

            if (type == typeof(decimal))
                return ((decimal)value).ToString(CultureInfo.InvariantCulture);

            if (type == typeof(double))
                return ((double)value).ToString(CultureInfo.InvariantCulture);

            if (type == typeof(float))
                return ((float)value).ToString(CultureInfo.InvariantCulture);

            if (type == typeof(bool))
                return (bool)value ? "true" : "false";

            if (type.IsEnum)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string Escape(string? value)
        {
            value ??= string.Empty;

            if (value.Contains('"'))
                value = value.Replace("\"", "\"\"");

            if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value}\"";

            return value;
        }
    }
}
