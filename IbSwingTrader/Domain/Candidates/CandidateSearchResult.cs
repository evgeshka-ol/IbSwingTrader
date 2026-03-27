
namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateSearchResult
    {
        public List<WishListItem> WishList { get; set; } = [];

        public List<CandidateDetails> Candidates { get; set; } = [];
    }
}