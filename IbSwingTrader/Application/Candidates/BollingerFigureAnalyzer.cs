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
            var midSlope = CalculateSlope(series.MidSeries);
            var widthSlope = CalculateSlope(series.WidthSeries);
            var upperDistanceSlope = CalculateSlope(series.UpperDistanceSeries);
            var latestUpperDistance = series.UpperDistanceSeries.Count > 0 ? series.UpperDistanceSeries[^1] : 0m;
            var priceRidingUpperBand = latestUpperDistance >= -1.0m;
            var pricePullingBackToMid = upperDistanceSlope < 0m && latestUpperDistance < -1.0m;
            var figureCollapsing = widthSlope < 0m && upperDistanceSlope < 0m;

            var direction = midSlope switch
            {
                > 0m => BollingerFigureDirection.Up,
                < 0m => BollingerFigureDirection.Down,
                _ => BollingerFigureDirection.Flat
            };

            var regime =
                priceRidingUpperBand && widthSlope > 0m
                    ? BollingerFigureRegime.Runaway
                : pricePullingBackToMid && direction != BollingerFigureDirection.Flat
                    ? BollingerFigureRegime.Pullback
                : figureCollapsing
                    ? BollingerFigureRegime.Collapse
                : upperDistanceSlope > 0m && widthSlope >= 0m
                    ? BollingerFigureRegime.Reacceleration
                : BollingerFigureRegime.Neutral;

            return new BollingerFigureState
            {
                Direction = direction,
                Regime = regime,
                MidSlope = midSlope,
                WidthSlope = widthSlope,
                UpperDistanceSlope = upperDistanceSlope,
                PriceToMidCompression = -upperDistanceSlope,
                PriceRidingUpperBand = priceRidingUpperBand,
                PricePullingBackToMid = pricePullingBackToMid,
                FigureCollapsing = figureCollapsing
            };
        }

        private static decimal CalculateSlope(List<decimal> series)
            => series.Count >= 2
                ? decimal.Round(series[^1] - series[0], 2, MidpointRounding.AwayFromZero)
                : 0m;
    }
}
