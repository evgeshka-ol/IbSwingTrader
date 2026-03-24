using IbSwingTrader.Models.Stocks;

namespace IbSwingTrader.Models
{
    public class CandidateSearchResult
    {
        public List<CandidateDetails> WishList { get; set; } = [];
        public List<CandidateDetails> Candidates { get; set; } = [];
    }
}
