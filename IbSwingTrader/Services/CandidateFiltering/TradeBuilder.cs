using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class TradeBuilder : ITradeBuilder
    {
        public TradePlan Build(List<Candle> candles)
        {
            if (candles == null || candles.Count < 10)
                throw new ArgumentException("Not enough candles");

            var last = candles[^1];

            // Берем минимум последних 5 свечей как опору для стопа
            var recentLow = candles
                .Skip(Math.Max(0, candles.Count - 5))
                .Min(x => x.Low);

            var entry = last.Close;

            // Небольшой отступ ниже локального минимума
            var stop = recentLow * 0.99m;

            // Защита от кривого плана
            if (stop >= entry)
                stop = entry * 0.97m;

            var risk = entry - stop;
            var exit = entry + risk * 2m; // RR 1:2

            return new TradePlan
            {
                Entry = entry,
                Stop = stop,
                Exit = exit
            };
        }
    }
}
