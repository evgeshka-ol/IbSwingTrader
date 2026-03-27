namespace IbSwingTrader.Domain.WishList
{
    public class AmbiguousBarResolutionResult
    {
        public bool ExitBeforeStop { get; set; }

        public DateTime? ExitTime { get; set; }

        public DateTime? StopTime { get; set; }
    }
}
