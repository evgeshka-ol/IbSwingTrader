namespace IbSwingTrader.Application.Candidates
{
    public enum BollingerFigureDirection
    {
        Flat = 0,
        Up = 1,
        Down = -1
    }

    public enum BollingerFigureRegime
    {
        Neutral = 0,
        Pullback = 1,
        Runaway = 2,
        Collapse = 3,
        Reacceleration = 4
    }

    public sealed class BollingerFigureState
    {
        public BollingerFigureDirection Direction { get; init; }
        public BollingerFigureRegime Regime { get; init; }
        public decimal MidSlope { get; init; }
        public decimal WidthSlope { get; init; }
        public decimal UpperDistanceSlope { get; init; }
        public decimal PriceToMidCompression { get; init; }
        public bool PriceRidingUpperBand { get; init; }
        public bool PricePullingBackToMid { get; init; }
        public bool FigureCollapsing { get; init; }
    }

    public sealed class BollingerFeatureSeries
    {
        public required List<decimal> MidSeries { get; init; }
        public required List<decimal> UpperDistanceSeries { get; init; }
        public required List<decimal> WidthSeries { get; init; }
    }

    public interface IBollingerFigureAnalyzer
    {
        BollingerFigureState Analyze(BollingerFeatureSeries series);
    }

    public sealed class BollingerFigureAnalyzer : IBollingerFigureAnalyzer
    {
        public BollingerFigureState Analyze(BollingerFeatureSeries series)
        {
            var midSlope = CalculateRelativeSlopePct(series.MidSeries);
            var upperSlope = CalculateRelativeSlopePct(series.UpperDistanceSeries);
            var lowerSlope = CalculateRelativeSlopePct(series.WidthSeries);
            var upperOpening = upperSlope > 0m;
            var midRising = midSlope > 0m;
            var lowerNotOutrunningMid = lowerSlope <= midSlope + 1.0m;
            var lowerOpeningDown = lowerSlope < 0m;
            var bandOpening = upperOpening && midRising && (lowerNotOutrunningMid || lowerOpeningDown);
            var bandClosing = upperSlope < 0m && lowerSlope > midSlope;
            var priceRidingUpperBand = bandOpening;
            var pricePullingBackToMid = midRising && upperSlope < 0m;
            var figureCollapsing = bandClosing || (!midRising && upperSlope < 0m);

            var direction = midSlope switch
            {
                > 0m => BollingerFigureDirection.Up,
                < 0m => BollingerFigureDirection.Down,
                _ => BollingerFigureDirection.Flat
            };

            var regime =
                bandOpening
                    ? BollingerFigureRegime.Runaway
                : pricePullingBackToMid && direction != BollingerFigureDirection.Flat
                    ? BollingerFigureRegime.Pullback
                : figureCollapsing
                    ? BollingerFigureRegime.Collapse
                : upperSlope > 0m && midSlope >= 0m
                    ? BollingerFigureRegime.Reacceleration
                : BollingerFigureRegime.Neutral;

            return new BollingerFigureState
            {
                Direction = direction,
                Regime = regime,
                MidSlope = midSlope,
                WidthSlope = lowerSlope,
                UpperDistanceSlope = upperSlope,
                PriceToMidCompression = midSlope - upperSlope,
                PriceRidingUpperBand = priceRidingUpperBand,
                PricePullingBackToMid = pricePullingBackToMid,
                FigureCollapsing = figureCollapsing
            };
        }

        private static decimal CalculateSlope(List<decimal> series)
            => series.Count >= 2
                ? decimal.Round(series[^1] - series[0], 2, MidpointRounding.AwayFromZero)
                : 0m;

        private static decimal CalculateRelativeSlopePct(List<decimal> series)
        {
            if (series.Count < 2)
                return 0m;

            var first = series[0];
            if (first == 0m)
                return CalculateSlope(series);

            return decimal.Round((series[^1] - first) / Math.Abs(first) * 100m, 2, MidpointRounding.AwayFromZero);
        }
    }
}
