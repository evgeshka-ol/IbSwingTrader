namespace IbSwingTrader.Abstractions.Logging
{
    public interface ICandidateFileService
    {
        Task<CandidateFileDocument> ReadAsync(string candidatesPath);

        Task WriteAsync(
            string candidatesPath,
            CandidateFileDocument document,
            IEnumerable<CandidateDetails> currentScanOutput);
    }
}
