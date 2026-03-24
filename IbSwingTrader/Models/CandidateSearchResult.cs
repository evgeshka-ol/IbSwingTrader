using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Models
{
    public class CandidateSearchResult
    {
        public List<WishListItem> WishList { get; set; } = [];

        public List<CandidateDetails> Candidates { get; set; } = [];
    }
}