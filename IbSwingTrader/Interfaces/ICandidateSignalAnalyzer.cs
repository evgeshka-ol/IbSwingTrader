using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateSignalAnalyzer
    {
        CandidateSignalSnapshot Analyze(List<Candle> candles);
    }
}
