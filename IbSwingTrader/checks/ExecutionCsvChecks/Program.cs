using IbSwingTrader.Abstractions.Logging;
using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Infrastructure.Logging;

var time = new DateTime(2026, 9, 21, 5, 3, 21).AddTicks(3188049);
var candidate = new CandidateDetails
{
    Ticker = "MSTR", CandidateSource = "SameDayContinuation",
    Scan = new ScanInfo { ScanTime = time.AddSeconds(-15), PublishedAt = time,
        FirstSeenAt = time.AddMinutes(-44), RunStartedAt = time.AddMinutes(-63) },
    TradePlan = new TradePlanInfo
    {
        LiveReferencePrice = 163.02m, EntryPrice = 163.02m, ExitPrice = 175.60m,
        ReferencePriceTime = time.AddSeconds(-3), ReferencePriceObservedAt = time.AddSeconds(-1),
        ReferencePriceSource = "M5PartialClose", PublicationRefreshStatus = "Refreshed",
        InitialEntryPrice = 162.49m, InitialReferencePrice = 162.49m,
        M5CurrentOpen = 162.95m, M5CurrentClose = 163.0197m,
        M5PreviousOpen = 162.72m, M5PreviousClose = 162.95m, M5ProjectedEntryPrice = 163.18m
    },
    Score = new(), Context = new()
};
var fmt = new NumberTextFormatter();
var properties = new ObjectPropertyReader();
var builder = new CandidateCsvRowBuilder(fmt, new ArrayCellFormatter(fmt), properties);
var service = new CandidateFileService(builder, properties, new SilentLogger());
var directory = Directory.CreateTempSubdirectory("execution-csv-checks-");
try
{
    var path = Path.Combine(directory.FullName, "candidates.csv");
    await service.WriteAsync(path, new CandidateFileDocument { SameDayCandidates = [candidate] }, [candidate]);
    var loaded = (await service.ReadAsync(path)).SameDayCandidates.Single();
    Check(loaded.Scan.PublishedAt == time, "Publication timestamp precision survives CSV");
    Check(loaded.Scan.FirstSeenAt == candidate.Scan.FirstSeenAt, "First detection survives CSV");
    Check(loaded.TradePlan.ReferencePriceTime == candidate.TradePlan.ReferencePriceTime, "Price cutoff survives CSV");
    Check(loaded.TradePlan.M5CurrentClose == 163.0197m, "Partial price precision survives CSV");
    Check(loaded.TradePlan.M5ProjectedEntryPrice == 163.18m && loaded.TradePlan.EntryPrice == 163.02m,
        "Shadow forecast and executable entry stay separate");
    Check(loaded.TradePlan.InitialEntryPrice == 162.49m, "Original plan remains available for comparison");

    await File.WriteAllTextAsync(path,
        "Ticker,ScanTime,CandidateGroup,CandidateSource,ScanPrice,EntryPrice\n" +
        "OLD,2026-09-18 05:03:06,Runaway,SameDayContinuation,100,99\n");
    var legacy = (await service.ReadAsync(path)).SameDayCandidates.Single();
    Check(legacy.Scan.PublishedAt == null && legacy.TradePlan.ReferencePriceTime == null,
        "Legacy rows do not acquire invented timestamps");
    Check(legacy.TradePlan.LiveReferencePrice == 100m && legacy.TradePlan.EntryPrice == 99m,
        "Legacy scalar aliases remain readable");
}
finally
{
    directory.Delete(recursive: true);
}
Console.WriteLine("Execution CSV checks passed.");

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

sealed class SilentLogger : ITextLogger
{
    public void EmptyLine() { }
    public void Info(string message) { }
    public void Debug(string message) { }
    public void Error(string message) { }
    public void InfoBlock(string title, string block) { }
    public void ErrorBlock(string title, string block) { }
    public void Warning(string message) { }
}
