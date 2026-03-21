using System.Text.Json;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class AgentSettingsProvider : IAgentSettingsProvider
    {
        private readonly AgentSettings _settings;

        public AgentSettingsProvider(string configPath)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    $"Agent settings file not found: {configPath}");

            var json = File.ReadAllText(configPath);

            _settings = JsonSerializer.Deserialize<AgentSettings>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize settings file: {configPath}");
        }

        public AgentSettings Get() => _settings;
    }
}
