using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class GetCandidatesCommand(
        ICandidateFinder finder,
        ICandidateResultWriter candidateWriter,
        IAgentPathService pathService,
        ILocalMarketScheduleProvider localMarketScheduleProvider,
        ITextLogger logger) : ICommand
    {
        private readonly ICandidateFinder _finder = finder;
        private readonly ICandidateResultWriter _candidateWriter = candidateWriter;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ILocalMarketScheduleProvider _localMarketScheduleProvider = localMarketScheduleProvider;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (!IsTodayTradingDay(out var marketToday))
            {
                _logger.Info(
                    $"GetCandidates skipped. Market date {marketToday:yyyy-MM-dd} is not a trading day.");
                return;
            }

            var result = await _finder.FindAsync();
            var candidatesPath = _pathService.GetCandidatesFile();

            var candidatesFolder = Path.GetDirectoryName(candidatesPath);
            if (!string.IsNullOrWhiteSpace(candidatesFolder))
                Directory.CreateDirectory(candidatesFolder);

            await _candidateWriter.WriteAsync(candidatesPath, result);

            _logger.Info($"Candidates saved: {candidatesPath}");
            _logger.Info(
                $"GetCandidates completed. " +
                $"Reversal ranking rows: {result.Candidates.Count}, " +
                $"Runaway ranking rows: {result.SameDayCandidates.Count}, " +
                $"Elapsed={ElapsedTimeFormatter.Format(stopwatch.Elapsed)}");
        }

        private bool IsTodayTradingDay(out DateTime marketToday)
        {
            marketToday = MarketTime.Now().Date;
            var scheduleStartUtc = DateTime.SpecifyKind(marketToday.AddDays(-1), DateTimeKind.Utc);
            var scheduleEndUtc = DateTime.SpecifyKind(marketToday.AddDays(2), DateTimeKind.Utc);
            var schedule = _localMarketScheduleProvider.BuildSchedule(scheduleStartUtc, scheduleEndUtc);
            var date = DateOnly.FromDateTime(marketToday);

            return schedule.Days.Any(x => x.Date == date && x.IsTradingDay);
        }
    }
}
