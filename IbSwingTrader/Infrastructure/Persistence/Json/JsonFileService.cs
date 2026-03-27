using System.Text.Json;

namespace IbSwingTrader.Infrastructure.Persistence.Json
{
    public class JsonFileService : IJsonFileService
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true
        };

        static JsonFileService()
        {
            Options.Converters.Add(new FlexibleDateTimeConverter());
            Options.Converters.Add(new FlexibleNullableDateTimeConverter());
        }

        public async Task<T?> ReadAsync<T>(string path)
        {
            if (!File.Exists(path))
                return default;

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        public async Task WriteAsync<T>(string path, T data)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, Options);
            await File.WriteAllTextAsync(path, json);
        }
    }
}
