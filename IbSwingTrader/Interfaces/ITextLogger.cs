namespace IbSwingTrader.Interfaces
{
    public interface ITextLogger
    {
        void EmptyLine();
        void Info(string message);
        void Debug(string message);
        void Error(string message);
        void InfoBlock(string title, string block);
        void ErrorBlock(string title, string block);
        void Warning(string message);
    }
}
