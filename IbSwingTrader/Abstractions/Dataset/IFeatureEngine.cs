
namespace IbSwingTrader.Abstractions.Dataset
{
    public interface IFeatureEngine
    {
        FeatureSet Calculate(List<Candle> candles, int index);
        FeatureSet CalculateLast(List<Candle> candles);
    }
}
