using IbSwingTrader.Commands;
using IbSwingTrader.Infrastructure.Bootstrap;
using IbSwingTrader.Interfaces;
using Microsoft.Extensions.DependencyInjection;

var serviceProvider = new ServiceCollection()
    .AddIbSwingTrader()
    .BuildServiceProvider();

using var scope = serviceProvider.CreateScope();
var services = scope.ServiceProvider;

var logger = services.GetRequiredService<ITextLogger>();

if (args.Length == 0)
{
    logger.Info("Usage:");
    logger.Info("  build-dataset <trades.csv> <dataset.csv>");
    logger.Info("  get-candidates");
    logger.Info("  evaluate-candidates");
    logger.Info("  get-scanner-params");
    logger.Info("  download-fundamental-snapshot");
    return;
}

var command = args[0];

switch (command)
{
    case "build-dataset":
        if (args.Length < 3)
        {
            logger.Info("Usage: build-dataset <trades.csv> <dataset.csv>");
            return;
        }

        await services
            .GetRequiredService<BuildDatasetCommand>()
            .RunAsync(args[1], args[2]);
        break;

    case "get-candidates":
        await services
            .GetRequiredService<GetCandidatesCommand>()
            .RunAsync();
        break;

    case "evaluate-candidates":
        await services
            .GetRequiredService<EvaluateCandidatesFolderCommand>()
            .RunAsync();
        break;

    case "get-scanner-params":
        await services
            .GetRequiredService<GetScannerParamsCommand>()
            .RunAsync();
        break;

    case "download-fundamental-snapshot":
        await services
            .GetRequiredService<DownloadFundamentalSnapshotCommand>()
            .RunAsync();
        break;

    default:
        logger.Error("Unknown command");
        break;
}