
namespace IbSwingTrader.Abstractions.Candidates
{
    public interface ICandidateFilter
    {
        bool Pass(
            CandidateSignalSnapshot snapshot,
            decimal price,
            decimal avgDollarVolumeDaily20);
    }
}
