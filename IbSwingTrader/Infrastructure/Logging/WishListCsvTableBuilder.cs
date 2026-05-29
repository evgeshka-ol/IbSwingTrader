using System.Collections;
using System.Globalization;

namespace IbSwingTrader.Infrastructure.Logging
{
    public static class WishListCsvTableBuilder
    {
        public static WishListCsvTable Build(
            IEnumerable<WishListItem> items,
            IObjectPropertyReader propertyReader)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(propertyReader);

            var headers = new List<string>();
            var rows = new List<Dictionary<string, string>>();

            foreach (var item in items
                         .OrderByDescending(x => x.Scan.ScanTime)
                         .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase))
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                Add(row, headers, "Ticker", item.Ticker);
                Add(row, headers, nameof(item.Scan.ScanTime), FormatValue(item.Scan.ScanTime));
                FlattenObject(row, headers, string.Empty, item.Scan, propertyReader);
                FlattenObject(row, headers, "Score", item.Score, propertyReader);
                FlattenObject(row, headers, "Context", item.Context, propertyReader);
                FlattenDirectProperties(row, headers, item, propertyReader);

                rows.Add(row);
            }

            return new WishListCsvTable(headers, rows);
        }

        private static void FlattenDirectProperties(
            Dictionary<string, string> row,
            List<string> headers,
            WishListItem item,
            IObjectPropertyReader propertyReader)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                nameof(WishListItem.Scan),
                nameof(WishListItem.Score),
                nameof(WishListItem.Context)
            };

            foreach (var property in propertyReader.GetOrderedProperties(typeof(WishListItem)))
            {
                if (skip.Contains(property.Name))
                    continue;

                Add(row, headers, property.Name, FormatValue(property.GetValue(item)));
            }
        }

        private static void FlattenObject(
            Dictionary<string, string> row,
            List<string> headers,
            string prefix,
            object value,
            IObjectPropertyReader propertyReader)
        {
            foreach (var property in propertyReader.GetOrderedProperties(value.GetType()))
            {
                var name = string.IsNullOrWhiteSpace(prefix)
                    ? property.Name
                    : $"{prefix}{property.Name}";

                Add(row, headers, name, FormatValue(property.GetValue(value)));
            }
        }

        private static void Add(Dictionary<string, string> row, List<string> headers, string name, string value)
        {
            if (!headers.Contains(name, StringComparer.OrdinalIgnoreCase))
                headers.Add(name);

            row[name] = value;
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            if (value is string s)
                return s;

            if (value is decimal d)
                return d.ToString("0.####", CultureInfo.InvariantCulture);

            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (value is bool b)
                return b ? "true" : "false";

            if (value is IEnumerable enumerable && value is not string)
            {
                var values = enumerable
                    .Cast<object?>()
                    .Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? string.Empty);

                return $"[{string.Join(" ", values)}]";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    public sealed record WishListCsvTable(
        List<string> Headers,
        List<Dictionary<string, string>> Rows);
}
