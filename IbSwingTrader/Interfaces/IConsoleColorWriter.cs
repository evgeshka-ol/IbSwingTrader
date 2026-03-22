namespace IbSwingTrader.Interfaces
{
    public interface IConsoleColorWriter
    {
        void Write(string text, ConsoleColor? color = null);
        void WriteLine(string text = "", ConsoleColor? color = null);
    }
}
