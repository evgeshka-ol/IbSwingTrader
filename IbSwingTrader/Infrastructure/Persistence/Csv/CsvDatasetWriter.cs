using System.Collections;
using System.Globalization;
using System.Text;

namespace IbSwingTrader.Infrastructure.Persistence.Csv
{
    public class CsvDatasetWriter(
        INumberTextFormatter numberFormatter,
        IArrayCellFormatter arrayCellFormatter,
        IObjectPropertyReader objectPropertyReader) : ICsvWriter
    {
        private readonly INumberTextFormatter _fmt = numberFormatter;
        private readonly IArrayCellFormatter _arrayFmt = arrayCellFormatter;
        private readonly IObjectPropertyReader _propertyReader = objectPropertyReader;

        public void Write<T>(string path, IEnumerable<T> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);

            var list = rows.ToList();

            if (list.Count == 0)
            {
                File.WriteAllText(path, string.Empty);
                return;
            }

            var properties = _propertyReader.GetOrderedProperties(typeof(T));

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

            if (value is IEnumerable<decimal> decimalValues)
                return _arrayFmt.Format(decimalValues);

            if (value is IEnumerable enumerable && value is not string)
            {
                var decimals = new List<decimal>();

                foreach (var item in enumerable)
                {
                    if (item is decimal dItem)
                        decimals.Add(dItem);
                }

                return _arrayFmt.Format(decimals);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }
    }
}