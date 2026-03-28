
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

            if (avgDollarVolumeDaily20 < s.MinDollarVolume)
            {
                _logger.Info("Entry rejected: low daily dollar volume.");
                return false;
            }

            if (f.DistanceTo20dHigh > s.MaxDistanceTo20dHigh)
            {
                _logger.Info("Entry rejected: too close to 20d high.");
                return false;
            }

            if (f.DailyMaSignedDistancePct < s.MinCurrentDailyMaSignedDistancePct)
            {
                _logger.Info("Entry rejected: current daily MA position is too weak.");
                return false;
            }

            var dailyTurnCount = 0;
            if (snapshot.DailyMaDelta3 > s.MinDailyMaDelta3) dailyTurnCount++;
            if (snapshot.DailyRsiDelta3 > s.MinDailyRsiDelta3) dailyTurnCount++;
            if (snapshot.DailyMacdDelta3 > s.MinDailyMacdDelta3) dailyTurnCount++;

            if (dailyTurnCount < s.MinDailyTurnSignals)
            {
                _logger.Info("Entry rejected: daily reversal is too weak.");
                return false;
            }

            var h4TurnCount = 0;
            if (snapshot.H4MaDelta3 > s.MinH4MaDelta3) h4TurnCount++;
            if (snapshot.H4RsiDelta3 > s.MinH4RsiDelta3) h4TurnCount++;
            if (snapshot.H4MacdDelta3 > s.MinH4MacdDelta3) h4TurnCount++;

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

            if (snapshot.DailyRsiDelta3 < s.MinDailyRsiDelta3Strong)
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
    }
}
