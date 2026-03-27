namespace IbSwingTrader.Abstractions.App
{
    public interface ICommand
    {
        Task RunAsync();
    }
}