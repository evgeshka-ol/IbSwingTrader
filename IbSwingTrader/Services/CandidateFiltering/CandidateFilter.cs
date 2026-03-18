using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFilter(ITextLogger logger) : ICandidateFilter
    {
        private readonly ITextLogger _logger = logger;

        public decimal MinPrice { get; set; } = 5m;
        public decimal MaxPrice { get; set; } = 200m;
        public decimal MinDollarVolume { get; set; } = 10_000_000m;

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

            // Хотим бумагу ниже средней, а не уже убежавшую вверх
            if (f.BBMidSignedDistancePct > -1m)
            {
                _logger.Info($"Filtered out due to BBMidSignedDistancePct {f.BBMidSignedDistancePct} above maximum -1");
                return false;
            }

            // Хотим не near-high momentum, а более глубокий откат
            if (f.DistanceTo20dHigh > -40m)
            {
                _logger.Info($"Filtered out due to DistanceTo20dHigh {f.DistanceTo20dHigh} above maximum -40");
                return false;
            }

            // Дневной контекст не должен быть перегрет
            if (f.DailyTrendPosition > 0.70m)
            {
                _logger.Info($"Filtered out due to DailyTrendPosition {f.DailyTrendPosition} above maximum 0.70");
                return false;
            }

            // Нужен вменяемый дневной pullback, а не хаос и не отсутствие отката
            if (f.DailyPullback10d < -50m || f.DailyPullback10d > -15m)
            {
                _logger.Info($"Filtered out due to DailyPullback10d {f.DailyPullback10d} outside range [-50; -15]");
                return false;
            }

            // Слишком большой volume spike не обязателен, часто даже мешает
            if (f.VolumeRatio20 > 1.0m)
            {
                _logger.Info($"Filtered out due to VolumeRatio20 {f.VolumeRatio20} above maximum 1.0");
                return false;
            }

            // Weekly momentum: лучше слабонегативный/переходный, а не already extended
            if (f.WeeklyMACDHistDelta < -1.0m || f.WeeklyMACDHistDelta > -0.05m)
            {
                _logger.Info($"Filtered out due to WeeklyMACDHistDelta {f.WeeklyMACDHistDelta} outside range [-1.0; -0.05]");
                return false;
            }

            // Ограничение по ATR можно оставить как защитное
            if (f.ATRRatio > 0.20m)
            {
                _logger.Info($"Filtered out due to ATRRatio {f.ATRRatio} above maximum 0.20");
                return false;
            }

            return true;
        }
    }
}
