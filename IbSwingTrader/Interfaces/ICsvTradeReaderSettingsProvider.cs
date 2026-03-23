using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICsvTradeReaderSettingsProvider
    {
        CsvTradeReaderSettings Get();
    }
}