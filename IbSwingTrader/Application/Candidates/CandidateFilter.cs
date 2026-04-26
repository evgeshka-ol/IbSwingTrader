using IbSwingTrader.Domain.Dataset;
namespace IbSwingTrader.Application.Candidates
{
    public class CandidateFilter(
        ITextLogger logger,
        IGetCandidatesSettingsProvider settingsProvider) : ICandidateFilter
    {
        private readonly ITextLogger _logger = logger;
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;

        public bool Pass(
            CandidateSignalSnapshot snapshot,
            decimal price,
            decimal avgDollarVolumeDaily20)
        {
            var s = _settingsProvider.Get().EntryFilter;
            var f = snapshot.Current;
            var dailyTurnCount = CountDailyTurnSignals(snapshot, s);
            var h4TurnCount = CountH4TurnSignals(snapshot, s);
            var isRecoveryCandidate =
                f.DistanceTo20dHigh <= -20m &&
                snapshot.DailyMaDelta3 > s.MinDailyMaDelta3 &&
                snapshot.DailyRsiDelta3 > s.MinDailyRsiDelta3 &&
                f.DailyRSI14 >= 30m &&
                (!f.WeeklyMACDLineMinusSignal.HasValue ||
                 f.WeeklyMACDLineMinusSignal.Value <= s.MaxCurrentWeeklyMacdLineMinusSignal);

            if (avgDollarVolumeDaily20 < s.MinDollarVolume)
            {
                _logger.Info("Entry rejected: low daily dollar volume.");
                return false;
            }

            if (PassProfile(f, dailyTurnCount, h4TurnCount, s.EarlyReversal))
            {
                _logger.Info("Entry accepted: early reversal profile.");
                return true;
            }

            if (PassProfile(f, dailyTurnCount, h4TurnCount, s.HotContinuation))
            {
                _logger.Info("Entry accepted: hot continuation profile.");
                return true;
            }

            if (f.DistanceTo20dHigh > s.MaxDistanceTo20dHigh)
            {
                _logger.Info("Entry rejected: too close to 20d high.");
                return false;
            }

            if (f.DailyMaSignedDistancePct < s.MinCurrentDailyMaSignedDistancePct && !isRecoveryCandidate)
            {
                _logger.Info("Entry rejected: current daily MA position is too weak.");
                return false;
            }

            if (dailyTurnCount < s.MinDailyTurnSignals)
            {
                _logger.Info("Entry rejected: daily reversal is too weak.");
                return false;
            }

            if (h4TurnCount < s.MinH4TurnSignals)
            {
                _logger.Info("Entry rejected: H4 reversal is too weak.");
                return false;
            }

            if (f.DailyRSI14 < s.MinCurrentDailyRsi14)
            {
                _logger.Info("Entry rejected: current daily RSI too weak.");
                return false;
            }

            if (snapshot.DailyRsiDelta3 < s.MinDailyRsiDelta3Strong && !isRecoveryCandidate)
            {
                _logger.Info("Entry rejected: daily RSI acceleration is too weak.");
                return false;
            }

            if (f.WeeklyMACDLineMinusSignal.HasValue &&
                f.WeeklyMACDLineMinusSignal.Value > s.MaxCurrentWeeklyMacdLineMinusSignal)
            {
                _logger.Info("Entry rejected: weekly MACD is too extended.");
                return false;
            }

            return true;
        }

        private static int CountDailyTurnSignals(CandidateSignalSnapshot snapshot, EntryFilterSettings settings)
        {
            var count = 0;
            if (snapshot.DailyMaDelta3 > settings.MinDailyMaDelta3) count++;
            if (snapshot.DailyRsiDelta3 > settings.MinDailyRsiDelta3) count++;
            if (snapshot.DailyMacdDelta3 > settings.MinDailyMacdDelta3) count++;
            return count;
        }

        private static int CountH4TurnSignals(CandidateSignalSnapshot snapshot, EntryFilterSettings settings)
        {
            var count = 0;
            if (snapshot.H4MaDelta3 > settings.MinH4MaDelta3) count++;
            if (snapshot.H4RsiDelta3 > settings.MinH4RsiDelta3) count++;
            if (snapshot.H4MacdDelta3 > settings.MinH4MacdDelta3) count++;
            return count;
        }

        private static bool PassProfile(
            FeatureSet current,
            int dailyTurnCount,
            int h4TurnCount,
            EntryProfileSettings profile)
        {
            if (!profile.Enabled)
                return false;

            if (current.DistanceTo20dHigh > profile.MaxDistanceTo20dHigh)
                return false;

            if (current.DailyMaSignedDistancePct < profile.MinCurrentDailyMaSignedDistancePct ||
                current.DailyMaSignedDistancePct > profile.MaxCurrentDailyMaSignedDistancePct)
            {
                return false;
            }

            if (current.DailyRSI14 < profile.MinCurrentDailyRsi14 ||
                current.DailyRSI14 > profile.MaxCurrentDailyRsi14)
            {
                return false;
            }

            if (dailyTurnCount < profile.MinDailyTurnSignals ||
                h4TurnCount < profile.MinH4TurnSignals)
            {
                return false;
            }

            if (current.WeeklyMACDLineMinusSignal.HasValue &&
                current.WeeklyMACDLineMinusSignal.Value > profile.MaxCurrentWeeklyMacdLineMinusSignal)
            {
                return false;
            }

            return true;
        }
    }
}
