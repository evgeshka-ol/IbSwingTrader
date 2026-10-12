namespace IbSwingTrader.Domain.Market
{
    public sealed record LiveQuote(decimal Price, DateTime PriceTimeUtc,
        DateTime ObservedAtUtc, string Source);
}
