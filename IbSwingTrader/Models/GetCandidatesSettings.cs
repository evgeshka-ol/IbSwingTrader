namespace IbSwingTrader.Models
{
    public class GetCandidatesSettings
    {
        public bool UseWishListFirst { get; set; } = true;
        public int RowsPerScan { get; set; } = 50;
        public int FinalTopCandidates { get; set; } = 10;
        public int MaxWishListItems { get; set; } = 100;
        public List<ScanCodeSettings> ScanCodes { get; set; } = [];
    }
}