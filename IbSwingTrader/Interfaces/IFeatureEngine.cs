using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IFeatureEngine
    {
        FeatureSet Calculate(List<Candle> candles, int index);
        FeatureSet CalculateLast(List<Candle> candles);
    }
}
