using System.Reflection;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class ObjectPropertyReader : IObjectPropertyReader
    {
        public PropertyInfo[] GetOrderedProperties(Type type)
        {
            var baseProps = type.BaseType?
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(p => p.MetadataToken)
                ?? Enumerable.Empty<PropertyInfo>();

            var ownProps = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.MetadataToken);

            return baseProps
                .Concat(ownProps)
                .ToArray();
        }
    }
}
