using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Interfaces
{
    public interface IAgentSettingsProvider
    {
        AgentSettings Get();
    }
}
