using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class ScanCodeInfoService(
        IGetCandidatesSettingsProvider settingsProvider) : IScanCodeInfoService
    {
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;

        public IReadOnlyList<PresetScanCode> GetAll()
        {
            return [.. _settingsProvider
                .Get()
                .ScanCodes
                .Select(x => new PresetScanCode(x.Code, x.Description))];
        }
    }
}