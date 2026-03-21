using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Commands
{
    public class GetScannerParamsCommand(
        ITwsConnection connection) : ICommand
    {
        public async Task RunAsync()
        {
            var xml = await connection.RequestScannerParametersAsync();

            await File.WriteAllTextAsync(
                "scanner_params.xml",
                xml);

            Console.WriteLine("Scanner parameters saved");
        }
    }
}
