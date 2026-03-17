using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFilter : ICandidateFilter
    {
        public decimal MinPrice { get; set; } = 5m;
        public decimal MaxPrice { get; set; } = 200m;
        public decimal MinDollarVolume { get; set; } = 10_000_000m;
        public decimal MinMarketCap { get; set; } = 300_000_000m;

        public bool Pass(
            FeatureSet f,
            decimal price,
            decimal avgVolume20,
            decimal marketCap)
        {
            if (price < MinPrice)
                return false;

            if (price > MaxPrice)
                return false;

            var dollarVolume = price * avgVolume20;

            if (dollarVolume < MinDollarVolume)
                return false;

            //if (marketCap < MinMarketCap)
            //    return false;

            if (f.VolumeRatio20 < 1.2m)
                return false;

            if (f.DistanceTo20dHigh < -15m)
                return false;

            if (f.ATRRatio > 0.12m)
                return false;

            return true;
        }
    }
}
