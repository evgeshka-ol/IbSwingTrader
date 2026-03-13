using System.Globalization;
using System.Reflection;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.MarketData.Csv
{
    public class CsvDatasetWriter : ICsvWriter
    {
        public void Write<T>(string path, IEnumerable<T> rows)
        {
            using var writer = new StreamWriter(path);

            var type = typeof(T);

            var baseProps = type.BaseType?
                .GetProperties()
                .OrderBy(p => p.MetadataToken)
                ?? Enumerable.Empty<PropertyInfo>();

            var ownProps = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.MetadataToken);

            var props = baseProps
                .Concat(ownProps)
                .ToArray();

            writer.WriteLine(string.Join(",", props.Select(p => p.Name)));

            foreach (var row in rows)
            {
                var values = props.Select(p =>
                {
                    var value = p.GetValue(row);

                    if (value == null)
                        return "";

                    if (value is decimal d)
                        return d.ToString("F6", CultureInfo.InvariantCulture);

                    if (value is DateTime dt)
                    {
                        if (p.Name.EndsWith("Date"))
                            return dt.ToString("yyyy-MM-dd");

                        return dt.ToString("yyyy-MM-dd HH:mm:ss");
                    }

                    return value.ToString();
                });

                writer.WriteLine(string.Join(",", values));
            }
        }
    }
}
