using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFinder(
        IStockUniverseProvider stockUniverseProvider,
        IStockPreFilter preFilter,
        IContractResolver contractResolver,
        IMarketDataProvider marketData,
        IFeatureEngine featureEngine,
        ICandidateFilter candidateFilter,
        ICandidateScore candidateScore,
        ITradeBuilder tradeBuilder,
        IScannerPresetService scannerPresets,
        ITextLogger logger) : ICandidateFinder
    {
        private readonly IStockUniverseProvider _stockUniverseProvider = stockUniverseProvider;
        private readonly IStockPreFilter _preFilter = preFilter;
        private readonly IContractResolver _contractResolver = contractResolver;
        private readonly IMarketDataProvider _marketData = marketData;
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ICandidateFilter _candidateFilter = candidateFilter;
        private readonly ICandidateScore _candidateScore = candidateScore;
        private readonly ITradeBuilder _tradeBuilder = tradeBuilder;
        private readonly IScannerPresetService _scannerPresets = scannerPresets;
        private readonly ITextLogger _logger = logger;

        public async Task<List<CandidateDetails>> FindAsync()
        {
            var results = new List<CandidateDetails>();

            foreach (var preset in _scannerPresets.GetAll())
            {
                var stocks = await _stockUniverseProvider.GetStocksAsync(preset.ScanCode);
                foreach (var stock in stocks)
                {
                    if (!_preFilter.Pass(stock))
                        continue;

                    Contract contract;

                    try
                    {
                        contract = await _contractResolver.ResolveStockAsync(stock.Ticker);
                    }
                    catch
                    {
                        continue;
                    }

                    var candles = await _marketData.GetCandles(
                        contract,
                        Timeframe.H4,
                        DateTime.UtcNow,
                        300);

                    if (candles.Count < 60)
                        continue;

                    var features = _featureEngine.CalculateLast(candles);

                    var price = candles[^1].Close;

                    var avgVolume20 = candles
                        .TakeLast(20)
                        .Average(x => x.Volume);

                    _logger.Info($"Candidate filter for: ticker ({stock.Ticker}), stock type ({stock.StockType}), trading class ({stock.TradingClass}), exchange ({stock.Exchange}), rank ({stock.Rank})");

                    if (!_candidateFilter.Pass(features, price, avgVolume20))
                        continue;

                    var score = _candidateScore.Calculate(features);
                    var trade = _tradeBuilder.Build(candles);

                    results.Add(new CandidateDetails
                    {
                        Ticker = stock.Ticker,

                        EntryPrice = trade.Entry,
                        ExitPrice = trade.Exit,
                        StopLoss = trade.Stop,

                        ProfitPercent = (trade.Exit - trade.Entry) / trade.Entry * 100m,
                        LossPercent = (trade.Stop - trade.Entry) / trade.Entry * 100m,

                        Score = score,

                        Pullback10d = features.Pullback10d,
                        DistanceTo20dHigh = features.DistanceTo20dHigh,
                        DistanceTo52wHigh = features.DistanceTo52wHigh,
                        VolumeRatio20 = features.VolumeRatio20,
                        ATRRatio = features.ATRRatio,
                        TrendPosition = features.TrendPosition,

                        ScanTime = DateTime.UtcNow,

                        // добавить
                        DailyTrendPosition = features.DailyTrendPosition,
                        DailyPullback10d = features.DailyPullback10d,
                        DailyRSI14 = features.DailyRSI14,
                        BBMidSignedDistancePct = features.BBMidSignedDistancePct,
                        WeeklyMACDHistDelta = features.WeeklyMACDHistDelta
                    });
                    _logger.Info($"Ticker {stock.Ticker} passed candidate filter.");
                }

            }

            return [.. results
                .OrderByDescending(x => x.Score)
                .Take(10)];
        }
    }
}
