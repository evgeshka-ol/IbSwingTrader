using System.Text.Json;
using System.Text.Json.Serialization;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class AgentSettingsProvider : IAgentSettingsProvider
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        private readonly AgentSettings _settings;

        public AgentSettingsProvider(string configPath)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    $"Agent settings file not found: '{configPath}'");

            var json = File.ReadAllText(configPath);

            _settings = JsonSerializer.Deserialize<AgentSettings>(
                json,
                JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize settings file: '{configPath}'");
        }

        public AgentSettings Get() => _settings;
    }
}