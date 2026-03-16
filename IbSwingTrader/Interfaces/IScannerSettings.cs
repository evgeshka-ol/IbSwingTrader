namespace IbSwingTrader.Interfaces
{
    public interface IScannerSettings
    {
        string LocationCode { get; }
        string ScanCode { get; }

        decimal MinPrice { get; }
        decimal MaxPrice { get; }

        decimal MinMarketCap { get; }
        long MinAvgVolume { get; }
    }
}
