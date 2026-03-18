using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class ScanCodeInfoService : IScanCodeInfoService
    {
        public IReadOnlyList<PresetScanCode> GetAll() =>
        [
            new("TOP_PERC_LOSE", "Top percentage losers. Better for watch list after sharp selloff."),
            new("TOP_PERC_GAIN", "Top percentage gainers. Momentum names already showing strength."),
            new("HOT_BY_VOLUME", "Most active by volume. Broad live universe for pullback setups."),
            new("MOST_ACTIVE", "Most active stocks. General liquid and active universe.")
        ];
    }
}
