namespace IbSwingTrader.Application.Dataset
{
    public static class EvaluationAmplitude
    {
        // Amplitude is a directionless range; ExtremumOrder remains the direction field.
        public static decimal WithDirection(decimal amplitude, DateTime? minTime, DateTime? maxTime)
        {
            return Math.Abs(amplitude);
        }
    }
}
