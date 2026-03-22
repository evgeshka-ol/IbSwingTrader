using System.Globalization;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.MarketData.Csv
{
    public class CsvDatasetWriter : ICsvWriter
    {
        private readonly IObjectPropertyReader _propertyReader;

        public CsvDatasetWriter(IObjectPropertyReader propertyReader)
        {
            _propertyReader = propertyReader;
        }

        public void Write<T>(string path, IEnumerable<T> rows)
        {
            using var writer = new StreamWriter(path);

            var props = _propertyReader.GetOrderedProperties(typeof(T));

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