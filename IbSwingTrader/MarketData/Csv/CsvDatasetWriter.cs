using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.MarketData.Csv
{
    public class CsvDatasetWriter(
        INumberTextFormatter numberFormatter) : ICsvWriter
    {
        private readonly INumberTextFormatter _fmt = numberFormatter;

        public void Write<T>(string path, IReadOnlyCollection<T> rows)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));

            var list = rows.ToList();

            if (list.Count == 0)
            {
                File.WriteAllText(path, string.Empty);
                return;
            }

            var properties = typeof(T)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead)
                .ToList();

            var sb = new StringBuilder();

            sb.AppendLine(string.Join(",", properties.Select(p => Escape(p.Name))));

            foreach (var row in list)
            {
                var values = properties
                    .Select(p => Escape(FormatValue(p.GetValue(row))))
                    .ToList();

                sb.AppendLine(string.Join(",", values));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            if (value is string s)
                return s;

            if (value is decimal d)
                return _fmt.Generic(d);

            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (value is bool b)
                return b ? "true" : "false";

            if (value is IEnumerable<decimal> decimalList)
                return FormatDecimalList(decimalList);

            if (value is IEnumerable enumerable && value is not string)
                return FormatEnumerable(enumerable);

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private string FormatDecimalList(IEnumerable<decimal> values)
        {
            return $"[{string.Join(" ", values.Select(v => _fmt.Generic(v)))}]";
        }

        private string FormatEnumerable(IEnumerable values)
        {
            var parts = new List<string>();

            foreach (var item in values)
            {
                if (item == null)
                    continue;

                if (item is decimal d)
                {
                    parts.Add(_fmt.Generic(d));
                    continue;
                }

                parts.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            return $"[{string.Join(" ", parts)}]";
        }

        private static string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }
    }
}