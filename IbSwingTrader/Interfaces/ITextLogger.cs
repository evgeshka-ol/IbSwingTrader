namespace IbSwingTrader.Interfaces
{
    public interface ITextLogger
    {
        void EmptyLine();
        void Info(string message);
        void Debug(string message);
        void Error(string message);
    }
}
