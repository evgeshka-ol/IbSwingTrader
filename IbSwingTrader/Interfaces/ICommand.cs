namespace IbSwingTrader.Interfaces
{
    public interface ICommand
    {
        Task RunAsync(params string[] args);
    }
}