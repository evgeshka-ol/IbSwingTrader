using System.Collections;
using System.Globalization;
using System.Text.Json.Nodes;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CompositePropertyJsonBuilder(
        IObjectPropertyReader propertyReader,
        INumberTextFormatter fmt) : ICompositePropertyJsonBuilder
    {
        private static readonly HashSet<string> PriceFields =
        [
            "EntryPrice",
            "ExitPrice",
            "StopLoss",
            "StopLimitPrice"
        ];

        private static readonly HashSet<string> PercentFields =
        [
            "ProfitPercent",
            "LossPercent"
        ];

        private static readonly HashSet<string> RatioFields =
        [
            "Score",
            "WeeklyScore",
            "DailyScore",
            "EntryScore",
            "DistanceTo20dHigh",
            "DistanceTo52wHigh",
            "DailyRSI14",
            "Pullback10d",
            "VolumeRatio20",
            "ATRRatio",
            "TrendPosition",
            "DailyTrendPosition",
            "DailyPullback10d",
            "BBMidSignedDistancePct",
            "WeeklyMACDHistDelta"
        ];

        private readonly IObjectPropertyReader _propertyReader = propertyReader;
        private readonly INumberTextFormatter _fmt = fmt;

        public JsonObject BuildObject(object source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return BuildObjectInternal(source, source.GetType());
        }

        private JsonObject BuildObjectInternal(object source, Type type)
        {
            var props = _propertyReader.GetOrderedProperties(type);
            var obj = new JsonObject();

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);
                obj[prop.Name] = ToJsonNode(value, prop.PropertyType, prop.Name);
            }

            return obj;
        }

        private JsonNode? ToJsonNode(object? value, Type valueType, string propertyName)
        {
            if (value == null)
                return null;

            var actualType = Nullable.GetUnderlyingType(valueType) ?? valueType;

            if (IsSimpleType(actualType))
                return ToSimpleJsonNode(value, propertyName);

            if (typeof(IEnumerable).IsAssignableFrom(actualType) && actualType != typeof(string))
                return BuildArray((IEnumerable)value);

            return BuildObjectInternal(value, actualType);
        }

        private JsonArray BuildArray(IEnumerable items)
        {
            var array = new JsonArray();

            foreach (var item in items)
            {
                if (item == null)
                {
                    array.Add(null);
                    continue;
                }

                var itemType = item.GetType();

                if (IsSimpleType(itemType))
                    array.Add(ToSimpleJsonNode(item, string.Empty));
                else
                    array.Add(BuildObjectInternal(item, itemType));
            }

            return array;
        }

        private JsonValue? ToSimpleJsonNode(object value, string propertyName)
        {
            return value switch
            {
                decimal d when PriceFields.Contains(propertyName)
                    => JsonValue.Create(_fmt.PriceValue(d)),

                decimal d when PercentFields.Contains(propertyName)
                    => JsonValue.Create(_fmt.PercentValue(d)),

                decimal d when RatioFields.Contains(propertyName)
                    => JsonValue.Create(_fmt.RatioValue(d)),

                decimal d
                    => JsonValue.Create(_fmt.GenericValue(d)),

                DateTime dt => JsonValue.Create(
                    propertyName.EndsWith("Date", StringComparison.OrdinalIgnoreCase)
                        ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),

                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                long l => JsonValue.Create(l),
                double d => JsonValue.Create(d),
                float f => JsonValue.Create(f),
                string s => JsonValue.Create(s),

                _ => JsonValue.Create(value.ToString())
            };
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(Guid);
        }
    }
}
