namespace IbSwingTrader.Domain.Settings
{
    public class CsvTradeReaderSettings
    {
        public string Separator { get; set; } = ";";

        public bool HasHeader { get; set; } = true;

        public List<CsvTradeReaderColumnSettings> Columns { get; set; } = [];
    }

    public class CsvTradeReaderColumnSettings
    {
        public string Name { get; set; } = string.Empty;

        public int Index { get; set; }
    }
}