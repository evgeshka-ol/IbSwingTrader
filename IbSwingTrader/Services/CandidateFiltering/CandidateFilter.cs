using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFilter(
        ITextLogger logger,
        IGetCandidatesSettingsProvider settingsProvider,
        INumberTextFormatter fmt) : ICandidateFilter
    {
        private readonly ITextLogger _logger = logger;
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;
        private readonly INumberTextFormatter _fmt = fmt;

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
                    $"Filtered out due to dollar volume {_fmt.Generic(dollarVolume)} below minimum {_fmt.Generic(settings.MinDollarVolume)}");
                return false;
            }

            if (featureSet.BBMidSignedDistancePct > settings.MaxBbMidSignedDistancePct)
            {
                _logger.Info(
                    $"Filtered out due to BBMidSignedDistancePct {_fmt.Ratio(featureSet.BBMidSignedDistancePct)} above {_fmt.Ratio(settings.MaxBbMidSignedDistancePct)}");
                return false;
            }

            if (featureSet.DistanceTo20dHigh > settings.MaxDistanceTo20dHigh)
            {
                _logger.Info(
                    $"Filtered out due to DistanceTo20dHigh {_fmt.Ratio(featureSet.DistanceTo20dHigh)} above {_fmt.Ratio(settings.MaxDistanceTo20dHigh)}");
                return false;
            }

            if (featureSet.ATRRatio > settings.MaxAtrRatio)
            {
                _logger.Info(
                    $"Filtered out due to ATRRatio {_fmt.Ratio(featureSet.ATRRatio)} above {_fmt.Ratio(settings.MaxAtrRatio)}");
                return false;
            }

            return true;
        }
    }
}