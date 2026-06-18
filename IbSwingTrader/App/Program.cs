try
{
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
        logger.Info("  build-research-dataset");
        logger.Info("  get-candidates");
        logger.Info("  evaluate-candidates");
        logger.Info("  normalize-reports");
        logger.Info("  evaluate-wishlist");
        logger.Info("  clean-up");
        logger.Info("  get-scanner-params");
        logger.Info("  download-fundamental-snapshot");
        return;
    }

    var command = args[0];

    switch (command)
    {
        case "build-dataset":
            await services
                .GetRequiredService<BuildDatasetCommand>()
                .RunAsync();
            break;

        case "build-research-dataset":
            await services
                .GetRequiredService<BuildResearchDatasetCommand>()
                .RunAsync();
            break;

        case "get-candidates":
            await services
                .GetRequiredService<GetCandidatesCommand>()
                .RunAsync();
            break;

        case "evaluate-candidates":
            await services
                .GetRequiredService<EvaluateCandidatesCommand>()
                .RunAsync();
            break;

        case "normalize-reports":
            await services
                .GetRequiredService<NormalizeReportsCommand>()
                .RunAsync();
            break;

        case "evaluate-wishlist":
            await services
                .GetRequiredService<EvaluateWishlistCommand>()
                .RunAsync();
            break;

        case "clean-up":
            await services
                .GetRequiredService<CleanUpCommand>()
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
}
catch (TwsConnectionException ex)
{
    var message =
        $"ERROR | Не удалось подключиться к TWS/IB Gateway по адресу {ex.Host}:{ex.Port}. " +
        "Запустите TWS или IB Gateway и проверьте, что API-подключения разрешены и порт указан верно.";

    Console.Error.WriteLine(message);

    try
    {
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, "startup-fatal.log");
        File.AppendAllText(
            fallbackPath,
            $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
    }
    catch
    {
        // Ignore fallback logging failures; the console output is the primary diagnostic path.
    }

    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    var message = $"FATAL STARTUP ERROR{Environment.NewLine}{ex}";
    Console.Error.WriteLine(message);

    try
    {
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, "startup-fatal.log");
        File.AppendAllText(
            fallbackPath,
            $"{DateTime.UtcNow:O} {message}{Environment.NewLine}{Environment.NewLine}");
    }
    catch
    {
        // Ignore fallback logging failures; the console output is the primary diagnostic path.
    }

    Environment.ExitCode = 1;
}
