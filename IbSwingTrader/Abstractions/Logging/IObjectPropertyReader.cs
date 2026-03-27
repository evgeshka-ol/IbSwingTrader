using System.Reflection;

namespace IbSwingTrader.Abstractions.Logging
{
    public interface IObjectPropertyReader
    {
        PropertyInfo[] GetOrderedProperties(Type type);
    }
}
