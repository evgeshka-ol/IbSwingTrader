using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Interfaces
{
    public interface ICsvTradeReaderSettingsProvider
    {
        CsvTradeReaderSettings Get();
    }
}