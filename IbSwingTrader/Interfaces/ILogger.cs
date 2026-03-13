namespace IbSwingTrader.Interfaces
{
    public interface ILogger
    {
        void EmptyLine();
        void Info(string message);
        void Debug(string message);
        void Error(string message);
    }
}
