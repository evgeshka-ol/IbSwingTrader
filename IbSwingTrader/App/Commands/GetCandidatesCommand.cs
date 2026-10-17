using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class GetCandidatesCommand(
        ICandidateFinder finder,
        ICandidateResultWriter candidateWriter,
        IAgentPathService pathService,
        ILocalMarketScheduleProvider localMarketScheduleProvider,
        ITwsConnection twsConnection,
        ITwsSettingsProvider twsSettingsProvider,
        ITextLogger logger) : ICommand
    {
        private readonly ICandidateFinder _finder = finder;
        private readonly ICandidateResultWriter _candidateWriter = candidateWriter;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ILocalMarketScheduleProvider _localMarketScheduleProvider = localMarketScheduleProvider;
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;
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

            // Check the broker before wishlist processing can treat a connection failure
            // as a per-ticker history error and retry it for every watched symbol.
            await EnsureConnectedAsync();

            var result = await _finder.FindAsync();
            var candidatesPath = _pathService.GetCandidatesFile();

            var candidatesFolder = Path.GetDirectoryName(candidatesPath);
            if (!string.IsNullOrWhiteSpace(candidatesFolder))
                Directory.CreateDirectory(candidatesFolder);

            await _candidateWriter.WriteAsync(candidatesPath, result);

            var reversalRankingRows = result.Candidates.Count(x => !CandidateGroups.IsOther(x));
            var runawayRankingRows = result.SameDayCandidates.Count(x => !CandidateGroups.IsOther(x));
            var otherRankingRows = result.Candidates.Concat(result.SameDayCandidates).Count(CandidateGroups.IsOther);

            _logger.Info($"Candidates saved: {candidatesPath}");
            _logger.Info(
                $"GetCandidates completed. " +
                $"ReversalHook ranking rows: {reversalRankingRows}, " +
                $"BellUp ranking rows: {runawayRankingRows}, " +
                $"Other ranking rows: {otherRankingRows}, " +
                $"Elapsed={ElapsedTimeFormatter.Format(stopwatch.Elapsed)}");
        }

        private async Task EnsureConnectedAsync()
        {
            var settings = _twsSettingsProvider.Get();
            if (!_twsConnection.IsConnected)
            {
                _logger.Info("Connecting to TWS...");
                _twsConnection.Connect(settings.Host, settings.Port, settings.ClientId);
            }

            try
            {
                var ready = await _twsConnection.Ready.Task.WaitAsync(
                    TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds));
                if (!ready || !_twsConnection.IsConnected)
                    throw new TwsConnectionException(settings.Host, settings.Port);
            }
            catch (TimeoutException ex)
            {
                throw new TwsConnectionException(settings.Host, settings.Port, ex);
            }

            _logger.Info("TWS connected.");
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
