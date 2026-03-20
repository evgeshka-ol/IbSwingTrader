using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFilter(ITextLogger logger) : ICandidateFilter
    {
        private readonly ITextLogger _logger = logger;

        public decimal MinDollarVolume { get; set; } = 1_000_000m;

        public bool Pass(
            FeatureSet featureSet,
            decimal price,
            decimal avgVolume20)
        {
            var dollarVolume = price * avgVolume20;

            if (dollarVolume < MinDollarVolume)
            {
                _logger.Info($"Filtered out due to dollar volume {dollarVolume} below minimum {MinDollarVolume}");
                return false;
            }

            if (featureSet.BBMidSignedDistancePct > 0.5m)
            {
                _logger.Info($"Filtered out due to BBMidSignedDistancePct {featureSet.BBMidSignedDistancePct} above 0.5");
                return false;
            }

            if (featureSet.DistanceTo20dHigh > -20m)
            {
                _logger.Info($"Filtered out due to DistanceTo20dHigh {featureSet.DistanceTo20dHigh} above -20");
                return false;
            }

            if (featureSet.ATRRatio > 0.20m)
            {
                _logger.Info($"Filtered out due to ATRRatio {featureSet.ATRRatio} above 0.20");
                return false;
            }

            return true;
        }
    }
}
