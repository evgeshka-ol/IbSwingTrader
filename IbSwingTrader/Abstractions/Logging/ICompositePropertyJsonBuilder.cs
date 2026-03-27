using System.Text.Json.Nodes;

namespace IbSwingTrader.Abstractions.Logging
{
    public interface ICompositePropertyJsonBuilder
    {
        JsonObject BuildObject(object source);
    }
}