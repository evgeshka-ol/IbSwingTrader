using System.Reflection;

namespace IbSwingTrader.Interfaces
{
    public interface IObjectPropertyReader
    {
        PropertyInfo[] GetOrderedProperties(Type type);
    }
}
