using System.Reflection;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class ObjectPropertyReader : IObjectPropertyReader
    {
        public PropertyInfo[] GetOrderedProperties(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            var hierarchy = new Stack<Type>();
            var current = type;

            while (current != null && current != typeof(object))
            {
                hierarchy.Push(current);
                current = current.BaseType;
            }

            return hierarchy
                .SelectMany(x => x
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .OrderBy(p => p.MetadataToken))
                .ToArray();
        }
    }
}
