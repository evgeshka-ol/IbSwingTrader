namespace IbSwingTrader.Interfaces
{
    public interface IScannerSettings
    {
        string LocationCode { get; }
        string ScanCode { get; }

        double MinPrice { get; }
        double MaxPrice { get; }

        double MinMarketCap { get; }
        int MinAvgVolume { get; }
    }
}
