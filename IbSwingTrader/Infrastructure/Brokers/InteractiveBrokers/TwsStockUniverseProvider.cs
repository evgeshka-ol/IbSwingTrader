using System.Globalization;
using IBApi;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public class TwsStockUniverseProvider(
        ITwsConnection tws) : IStockUniverseProvider
    {
        private readonly ITwsConnection _twsConnection = tws;

        private const double MinMarketCap = 300_000_000;

        private string LocationCode { get; set; } = "STK.US.MAJOR";

        // Было TOP_PERC_GAIN TOP_PERC_LOSE
        private double MinPrice { get; set; } = 5;
        // 200 was clipping legitimate live winners like AAOI/NBIS before they even reached scanner logic.
        private double MaxPrice { get; set; } = 300;

        private int MinAvgVolume { get; set; } = 1_000_000;

        public async Task<List<StockInfo>> GetStocksAsync(string scanCode)
        {
            var subscription = new ScannerSubscription
            {
                Instrument = "STK",
                LocationCode = LocationCode,
                ScanCode = scanCode,

                // IB scanner uses reversed market cap semantics here.
                // MarketCapBelow acts like our minimum market cap threshold.
                // MarketCapBelow = MinMarketCap, // temorarily disabled market cap to extend the range for analysis purposes

                StockTypeFilter = "CORP"
            };

            var filters = new List<TagValue>();

            if (MinPrice > 0)
                filters.Add(new TagValue("priceAbove", MinPrice.ToString(CultureInfo.InvariantCulture)));

            if (MaxPrice > 0)
                filters.Add(new TagValue("priceBelow", MaxPrice.ToString(CultureInfo.InvariantCulture)));

            if (MinAvgVolume > 0)
                filters.Add(new TagValue("avgVolumeAbove", MinAvgVolume.ToString(CultureInfo.InvariantCulture)));

            return await _twsConnection.GetStocksAsync(subscription, filters);
        }
    }
}
