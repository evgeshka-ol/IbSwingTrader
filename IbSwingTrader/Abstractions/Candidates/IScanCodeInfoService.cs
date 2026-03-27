
namespace IbSwingTrader.Abstractions.Candidates
{
    public interface IScanCodeInfoService
    {
        IReadOnlyList<PresetScanCode> GetAll();
    }
}