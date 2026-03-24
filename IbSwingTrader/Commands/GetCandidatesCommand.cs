using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Commands
{
    public class GetCandidatesCommand(
        ICandidateFinder finder,
        ICandidateResultWriter writer,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private readonly ICandidateFinder _finder = finder;
        private readonly ICandidateResultWriter _writer = writer;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var result = await _finder.FindAsync();

            var now = DateTime.UtcNow;
            var stamp = now.ToString("yyyyMMdd_HHmm");

            var candidatesFolder = _pathService.GetCandidatesFolder();
            Directory.CreateDirectory(candidatesFolder);

            var candidatesPath = Path.Combine(
                candidatesFolder,
                $"candidates_{stamp}_MARKET.json");

            var wishListPath = _pathService.GetWishListFile();

            var wishListFolder = Path.GetDirectoryName(wishListPath);
            if (!string.IsNullOrWhiteSpace(wishListFolder))
                Directory.CreateDirectory(wishListFolder);

            await _writer.WriteAsync(wishListPath, result.WishList);
            await _writer.WriteAsync(candidatesPath, result.Candidates);

            _logger.Info($"Wish list saved: {wishListPath}");
            _logger.Info($"Candidates saved: {candidatesPath}");
            _logger.Info(
                $"GetCandidates completed. " +
                $"Wish list count: {result.WishList.Count}, " +
                $"Candidates count: {result.Candidates.Count}");
        }
    }
}