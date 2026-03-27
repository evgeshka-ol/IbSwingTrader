
namespace IbSwingTrader.Application.Candidates
{
    public class CandidateSignalAnalyzer(
            IFeatureEngine featureEngine) : ICandidateSignalAnalyzer
    {
        private readonly IFeatureEngine _featureEngine = featureEngine;

        public CandidateSignalSnapshot Analyze(List<Candle> candles)
        {
            ArgumentNullException.ThrowIfNull(candles);

            if (candles.Count < 80)
                throw new ArgumentException("Not enough candles for signal analysis.", nameof(candles));

            var last = candles.Count;

            return new CandidateSignalSnapshot
            {
                Current = _featureEngine.Calculate(candles, last),
                Prev1 = _featureEngine.Calculate(candles, last - 1),
                Prev2 = _featureEngine.Calculate(candles, last - 2),
                Prev3 = _featureEngine.Calculate(candles, last - 3),
            };
        }
    }
}
