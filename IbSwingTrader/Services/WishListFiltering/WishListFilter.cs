using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.WishListFiltering
{
    public class WishListFilter(
        ITextLogger logger,
        IGetCandidatesSettingsProvider settingsProvider,
        INumberTextFormatter fmt) : IWishListFilter
    {
        private readonly ITextLogger _logger = logger;
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;
        private readonly INumberTextFormatter _fmt = fmt;

        public bool Pass(
            CandidateSignalSnapshot snapshot,
            decimal price,
            decimal avgDollarVolumeDaily20)
        {
            var s = _settingsProvider.Get().WishListFilter;
            var f = snapshot.Current;

            if (avgDollarVolumeDaily20 < s.MinDollarVolume)
            {
                _logger.Info(
                    $"WishList rejected: dollar volume {_fmt.Generic(avgDollarVolumeDaily20)} < {_fmt.Generic(s.MinDollarVolume)}");
                return false;
            }

            if (f.DailyMaSignedDistancePct >= 0m)
            {
                _logger.Info(
                    $"WishList rejected: price is not below daily Bollinger mid. " +
                    $"Ticker price={_fmt.Generic(price)}, daily distance={_fmt.Generic(f.DailyMaSignedDistancePct)}%");
                return false;
            }

            if (!f.WeeklyMaSignedDistancePct.HasValue)
            {
                _logger.Info("WishList rejected: weekly Bollinger mid is unavailable.");
                return false;
            }

            if (f.WeeklyMaSignedDistancePct.Value >= 0m)
            {
                _logger.Info(
                    $"WishList rejected: price is not below weekly Bollinger mid. " +
                    $"Ticker price={_fmt.Generic(price)}, weekly distance={_fmt.Generic(f.WeeklyMaSignedDistancePct.Value)}%");
                return false;
            }

            return true;
        }
    }
}