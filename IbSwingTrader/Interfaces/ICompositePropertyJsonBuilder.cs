using System.Text.Json.Nodes;

namespace IbSwingTrader.Interfaces
{
    public interface ICompositePropertyJsonBuilder
    {
        JsonObject BuildObject(object source);
    }
}