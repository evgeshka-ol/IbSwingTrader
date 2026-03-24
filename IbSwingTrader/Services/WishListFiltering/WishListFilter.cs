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
                _logger.Info($"WishList rejected: dollar volume {_fmt.Generic(avgDollarVolumeDaily20)} < {_fmt.Generic(s.MinDollarVolume)}");
                return false;
            }

            if (f.DistanceTo20dHigh > s.MaxDistanceTo20dHigh)
                return false;

            if (f.DistanceTo52wHigh > s.MaxDistanceTo52wHigh)
                return false;

            if (f.DailyMaSignedDistancePct > s.MaxDailyMaSignedDistancePct)
                return false;

            if (f.DailyRSI14 < s.MinDailyRsi14 || f.DailyRSI14 > s.MaxDailyRsi14)
                return false;

            if (f.DailyMACDLineMinusSignal > s.MaxDailyMacdLineMinusSignal)
                return false;

            if (f.WeeklyMaSignedDistancePct.HasValue &&
                f.WeeklyMaSignedDistancePct.Value < s.MinWeeklyMaSignedDistancePct)
                return false;

            if (f.WeeklyRSI14.HasValue &&
                f.WeeklyRSI14.Value < s.MinWeeklyRsi14)
                return false;

            return true;
        }
    }
}
