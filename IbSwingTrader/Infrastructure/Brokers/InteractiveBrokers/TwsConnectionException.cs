namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public sealed class TwsConnectionException(
        string host,
        int port,
        Exception? innerException = null)
        : Exception($"Unable to connect to TWS/IB Gateway at {host}:{port}.", innerException)
    {
        public string Host { get; } = host;

        public int Port { get; } = port;
    }
}
