
namespace IbSwingTrader.Abstractions.Candidates
{
    public interface ICandidateScore
    {
        decimal Calculate(CandidateSignalSnapshot snapshot);
    }
}
