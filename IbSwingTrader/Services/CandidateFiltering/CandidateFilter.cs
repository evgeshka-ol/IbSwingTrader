using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFilter(
        ITextLogger logger,
        IGetCandidatesSettingsProvider settingsProvider) : ICandidateFilter
    {
        private readonly ITextLogger _logger = logger;
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;

        public bool Pass(
            FeatureSet featureSet,
            decimal price,
            decimal avgVolume20)
        {
            var settings = _settingsProvider.Get().CandidateFilter;
            var dollarVolume = price * avgVolume20;

            if (dollarVolume < settings.MinDollarVolume)
            {
                _logger.Info(
                    $"Filtered out due to dollar volume {dollarVolume} below minimum {settings.MinDollarVolume}");
                return false;
            }

            if (featureSet.BBMidSignedDistancePct > settings.MaxBbMidSignedDistancePct)
            {
                _logger.Info(
                    $"Filtered out due to BBMidSignedDistancePct {featureSet.BBMidSignedDistancePct} above {settings.MaxBbMidSignedDistancePct}");
                return false;
            }

            if (featureSet.DistanceTo20dHigh > settings.MaxDistanceTo20dHigh)
            {
                _logger.Info(
                    $"Filtered out due to DistanceTo20dHigh {featureSet.DistanceTo20dHigh} above {settings.MaxDistanceTo20dHigh}");
                return false;
            }

            if (featureSet.ATRRatio > settings.MaxAtrRatio)
            {
                _logger.Info(
                    $"Filtered out due to ATRRatio {featureSet.ATRRatio} above {settings.MaxAtrRatio}");
                return false;
            }

            return true;
        }
    }
}