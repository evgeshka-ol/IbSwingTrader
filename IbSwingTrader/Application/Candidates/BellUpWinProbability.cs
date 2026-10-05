namespace IbSwingTrader.Application.Candidates
{
    public static class BellUpWinProbability
    {
        public const string ModelVersion = "bellup-win-ridge-2026-10-05-v1";
        public const string Scope = "WinGivenEntryAndResolution";

        // Exploratory ridge logistic model: 46 resolved replay plans / 28 tickers.
        // Labels use the reduced-target policy. Each ticker has total training weight 1.
        // L2=4; intercept has a symmetric two-observation prior. Not holdout calibrated.
        public static decimal? Estimate(CandidateDetails candidate)
        {
            var plan = candidate.TradePlan;
            var diagnostics = candidate.Diagnostics;
            if (!candidate.CandidateSource.Equals("BellUp", StringComparison.OrdinalIgnoreCase) ||
                diagnostics == null || plan.EntryPrice <= 0m || plan.ExitPrice <= plan.EntryPrice ||
                plan.StopLoss <= 0m || plan.StopLoss >= plan.EntryPrice ||
                candidate.RecentH4RsiSeries.Count < 4 || diagnostics.VolumeRatio20 < 0m)
                return null;

            var profit = Math.Clamp((double)((plan.ExitPrice / plan.EntryPrice - 1m) * 100m), 0d, 20d) / 5d;
            var risk = Math.Clamp((double)((1m - plan.StopLoss / plan.EntryPrice) * 100m), 0d, 20d) / 5d;
            var volume = Math.Clamp((double)diagnostics.VolumeRatio20, 0d, 3d) / 0.5d;
            var rsi = candidate.RecentH4RsiSeries;
            var rsiChange = Math.Clamp((double)(rsi[^1] - rsi[^4]), -30d, 30d) / 10d;
            var logOdds = 0.27260480847132856d
                - 0.2283064193053738d * profit
                + 0.21007861807436817d * risk
                + 0.5286783556166297d * volume
                + 0.29575989699829874d * rsiChange;
            return decimal.Round((decimal)(100d / (1d + Math.Exp(-logOdds))), 1,
                MidpointRounding.AwayFromZero);
        }
    }
}
