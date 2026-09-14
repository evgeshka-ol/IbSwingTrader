namespace IbSwingTrader.Application.Dataset
{
    public static class EvaluationAmplitude
    {
        // Sign describes the order of extrema; magnitude remains the existing range calculation.
        public static decimal WithDirection(decimal amplitude, DateTime? minTime, DateTime? maxTime)
        {
            var magnitude = Math.Abs(amplitude);
            return minTime.HasValue && maxTime.HasValue && maxTime.Value < minTime.Value
                ? -magnitude
                : magnitude;
        }
    }
}
