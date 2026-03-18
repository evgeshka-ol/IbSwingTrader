using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFilter(ITextLogger logger) : ICandidateFilter
    {
        private readonly ITextLogger _logger = logger;

        public decimal MinPrice { get; set; } = 5m;
        public decimal MaxPrice { get; set; } = 200m;
        public decimal MinDollarVolume { get; set; } = 5_000_000m;

        public bool Pass(
            FeatureSet f,
            decimal price,
            decimal avgVolume20)
        {
            if (price < MinPrice)
            {
                _logger.Info($"Filtered out due to price {price} below minimum {MinPrice}");
                return false;
            }

            if (price > MaxPrice)
            {
                _logger.Info($"Filtered out due to price {price} above maximum {MaxPrice}");
                return false;
            }

            var dollarVolume = price * avgVolume20;

            if (dollarVolume < MinDollarVolume)
            {
                _logger.Info($"Filtered out due to dollar volume {dollarVolume} below minimum {MinDollarVolume}");
                return false;
            }

            if (f.BBMidSignedDistancePct > 0.5m)
            {
                _logger.Info($"Filtered out due to BBMidSignedDistancePct {f.BBMidSignedDistancePct} above 0.5");
                return false;
            }

            if (f.DistanceTo20dHigh > -20m)
            {
                _logger.Info($"Filtered out due to DistanceTo20dHigh {f.DistanceTo20dHigh} above -20");
                return false;
            }

            if (f.ATRRatio > 0.20m)
            {
                _logger.Info($"Filtered out due to ATRRatio {f.ATRRatio} above 0.20");
                return false;
            }

            return true;
        }
    }
}
