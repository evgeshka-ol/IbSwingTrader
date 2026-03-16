using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsStockUniverseProvider(
        ITwsConnection tws,
        IScannerSettings settings) : IStockUniverseProvider
    {
        private readonly ITwsConnection _twsConnection = tws;
        private readonly IScannerSettings _settings = settings;

        public async Task<List<StockInfo>> GetStocksAsync()
        {
            var subscription = new ScannerSubscription
            {
                Instrument = "STK",
                LocationCode = _settings.LocationCode,
                ScanCode = _settings.ScanCode
            };

            var filters = new List<TagValue>();

            if (_settings.MinPrice > 0)
                filters.Add(new TagValue("priceAbove", _settings.MinPrice.ToString()));

            if (_settings.MaxPrice > 0)
                filters.Add(new TagValue("priceBelow", _settings.MaxPrice.ToString()));

            if (_settings.MinMarketCap > 0)
                filters.Add(new TagValue("marketCapAbove", _settings.MinMarketCap.ToString()));

            if (_settings.MinAvgVolume > 0)
                filters.Add(new TagValue("avgVolumeAbove", _settings.MinAvgVolume.ToString()));

            return await _twsConnection.GetStocksAsync(subscription, filters);
        }
    }
}
