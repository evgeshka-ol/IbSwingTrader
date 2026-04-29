using IbSwingTrader.Domain.Settings;
namespace IbSwingTrader.Application.WishList
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

            var isDeepLaunch = PassProfile(snapshot, s.DeepLaunchProfile);
            var isExplosiveBreakout = PassProfile(snapshot, s.ExplosiveBreakoutProfile);
            var allowProfileBypass = isDeepLaunch || isExplosiveBreakout;

            if (f.DailyMaSignedDistancePct > s.MaxDailyMaSignedDistancePct && !allowProfileBypass)
            {
                _logger.Info(
                    $"WishList rejected: price is too far above daily MA baseline. " +
                    $"Ticker price={_fmt.Generic(price)}, daily distance={_fmt.Generic(f.DailyMaSignedDistancePct)}%");
                return false;
            }

            if (!f.WeeklyMaSignedDistancePct.HasValue)
            {
                if (allowProfileBypass)
                    return true;

                _logger.Info("WishList rejected: weekly MA baseline is unavailable.");
                return false;
            }

            if (f.WeeklyMaSignedDistancePct.Value > s.MaxWeeklyMaSignedDistancePct && !allowProfileBypass)
            {
                _logger.Info(
                    $"WishList rejected: price is too far above weekly MA baseline. " +
                    $"Ticker price={_fmt.Generic(price)}, weekly distance={_fmt.Generic(f.WeeklyMaSignedDistancePct.Value)}%");
                return false;
            }

            return true;
        }

        private static bool PassProfile(
            CandidateSignalSnapshot snapshot,
            WishListProfileSettings profile)
        {
            if (!profile.Enabled)
                return false;

            var f = snapshot.Current;

            if (f.DistanceTo20dHigh > profile.MaxDistanceTo20dHigh)
                return false;

            if (f.DailyBollingerBandWidthPct < profile.MinDailyBollingerBandWidthPct)
                return false;

            if ((f.WeeklyBollingerBandWidthPct ?? decimal.MinValue) < profile.MinWeeklyBollingerBandWidthPct)
                return false;

            if (f.DailyRSI14 < profile.MinCurrentDailyRsi14 ||
                f.DailyRSI14 > profile.MaxCurrentDailyRsi14)
            {
                return false;
            }

            if (snapshot.DailyMaDelta3 < profile.MinDailyMaDelta3 ||
                snapshot.DailyRsiDelta3 < profile.MinDailyRsiDelta3 ||
                f.DailyMACDLineMinusSignal < profile.MinDailyMacdLineMinusSignal)
            {
                return false;
            }

            if (f.WeeklyMACDLineMinusSignal.HasValue &&
                f.WeeklyMACDLineMinusSignal.Value > profile.MaxCurrentWeeklyMacdLineMinusSignal)
            {
                return false;
            }

            return true;
        }
    }
}
