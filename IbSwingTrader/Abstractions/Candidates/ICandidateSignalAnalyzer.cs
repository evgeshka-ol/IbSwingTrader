
namespace IbSwingTrader.Abstractions.Candidates
{
    public interface ICandidateSignalAnalyzer
    {
        CandidateSignalSnapshot Analyze(List<Candle> candles);
    }
}
