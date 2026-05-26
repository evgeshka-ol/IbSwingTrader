namespace IbSwingTrader.Abstractions.Logging
{
    public interface ICandidateCsvRowBuilder
    {
        CandidateCsvTable Build(
            IEnumerable<CandidateDetails> candidates,
            IEnumerable<CandidateDetails> sameDayCandidates,
            IReadOnlySet<string> currentOperationKeys);
    }

    public sealed class CandidateCsvTable
    {
        public List<string> Headers { get; init; } = [];

        public List<Dictionary<string, string>> Rows { get; init; } = [];
    }
}
