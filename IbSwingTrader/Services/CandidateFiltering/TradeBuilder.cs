using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class TradeBuilder(IGetCandidatesSettingsProvider settingsProvider) : ITradeBuilder
    {
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;

        public TradePlan Build(List<Candle> candles)
        {
            var settings = _settingsProvider.Get().TradePlan;

            if (candles == null || candles.Count < settings.MinimumCandles)
                throw new ArgumentException("Not enough candles");

            var last = candles[^1];

            var recentLow = candles
                .Skip(Math.Max(0, candles.Count - settings.StopLookbackBars))
                .Min(x => x.Low);

            var entry = last.Close;
            var stop = recentLow * settings.StopBufferMultiplier;

            if (stop >= entry)
                stop = entry * settings.FallbackStopMultiplier;

            var risk = entry - stop;
            var exit = entry + risk * settings.RiskRewardRatio;

            return new TradePlan
            {
                Entry = entry,
                Stop = stop,
                Exit = exit
            };
        }
    }
}