using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class TradeBuilder : ITradeBuilder
    {
        public TradePlan Build(List<Candle> candles)
        {
            var last = candles[^1];

            var entry = last.Close;

            var stop = entry * 0.95m;

            var exit = entry * 1.15m;

            return new TradePlan
            {
                Entry = entry,
                Exit = exit,
                Stop = stop
            };
        }
    }
}
