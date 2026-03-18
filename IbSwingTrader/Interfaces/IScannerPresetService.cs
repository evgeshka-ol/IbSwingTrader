using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IScannerPresetService
    {
        IReadOnlyList<ScannerPreset> GetAll();
    }
}