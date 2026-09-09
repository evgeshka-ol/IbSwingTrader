namespace IbSwingTrader.Domain.Candidates
{
    public static class CandidateGroups
    {
        public static bool IsOther(CandidateDetails candidate) =>
            string.Equals(candidate.CandidateSource, "Other", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.CandidateSource, "DiagnosticRejected", StringComparison.OrdinalIgnoreCase);
    }
}
