using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IScanCodeInfoService
    {
        IReadOnlyList<PresetScanCode> GetAll();
    }
}