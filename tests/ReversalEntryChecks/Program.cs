using IbSwingTrader.Abstractions.Logging;
using IbSwingTrader.Abstractions.Settings;
using IbSwingTrader.Application.Candidates;
using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Domain.Market;
using IbSwingTrader.Domain.Settings;
using IbSwingTrader.Infrastructure.Logging;
using IbSwingTrader.Infrastructure.Settings;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
var config = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../../IbSwingTrader/agentsettings.json"));
var settings = new AgentSettingsProvider(config).Get().GetCandidates;
var builder = new TradeBuilder(new SavedSettings(settings), new NumberTextFormatter(), new SilentLogger());
List<Candle> History(decimal scan) => Enumerable.Range(0, 80).Select(i => new Candle
{
    Time = new DateTime(2026, 10, 1).AddHours(4 * i), Timeframe = Timeframe.H4,
    Open = scan * .95m, Close = scan * .95m, High = scan * 1.02m, Low = scan * .85m,
    Volume = 100000
}).ToList();
List<Candle> OldM15(decimal scan) => Enumerable.Range(0, 50).Select(i => new Candle
{
    Time = new DateTime(2026, 10, 26, 4, 0, 0).AddMinutes(15 * i), Timeframe = Timeframe.M15,
    Open = scan * .98m, Close = scan * .98m, High = scan * .981m, Low = scan * .979m,
    Volume = 1000
}).ToList();

foreach (var (ticker, scan) in new[] { ("AAL", 12.21m), ("NVDA", 216.40m),
    ("INTC", 91.37m), ("AAOI", 72.91m), ("FractionalQuote", 9.1234m) })
{
    // Synthetic histories deliberately differ from the fresh scan reference.
    // Even a supplied legacy discount/floor must not override direct entry.
    var plan = builder.Build(History(scan), OldM15(scan), scanPriceOverride: scan,
        scanPriceFloorOverride: scan * 1.10m, entryDiscountOverridePct: .07m,
        entryPatternFamily: TradeEntryPatternFamily.ReversalHook,
        defaultProfitPctOverride: .08m, minProfitPctOverride: .05m,
        maxProfitPctOverride: .10m, maxLossPctOverride: .06m, useScanPriceEntry: true);
    Check(plan.Entry == scan, $"{ticker}: fresh ScanPrice wins over M15, floor, historical H4 and DeepPullback");
    Check(plan.Exit > scan, $"{ticker}: target rebuilt above the fresh entry");
    Check(plan.Stop > 0m && plan.Stop < scan && (1m - plan.Stop / scan) <= .06m,
        $"{ticker}: stop rebuilt with the entry-relative loss cap");
    Check(plan.StopLimit > 0m && plan.StopLimit < plan.Stop, $"{ticker}: valid stop-limit ordering");
}
foreach (var missing in new decimal?[] { null, 0m, -1m })
{
    var rejected = false;
    try
    {
        builder.Build(History(100m), scanPriceOverride: missing,
            entryPatternFamily: TradeEntryPatternFamily.ReversalHook, useScanPriceEntry: true);
    }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "A direct ReversalHook entry cannot fall back to a historical close");
}
var legacy = builder.Build(History(100m), [], scanPriceOverride: 100m,
    entryDiscountOverridePct: .07m, entryPatternFamily: TradeEntryPatternFamily.None);
Check(legacy.Entry == 93m, "Non-publication legacy callers retain their explicitly requested policy");
var directWithoutM15 = builder.Build(History(100m), [], scanPriceOverride: 100m,
    entryPatternFamily: TradeEntryPatternFamily.ReversalHook, useScanPriceEntry: true);
Check(directWithoutM15.Entry == 100m && directWithoutM15.Exit > 100m,
    "Fresh reversal entry and rebuilt target do not require M15 entry forecasting");
Console.WriteLine($"Passed {checks} reversal entry checks.");

sealed class SavedSettings(GetCandidatesSettings settings) : IGetCandidatesSettingsProvider
{
    public GetCandidatesSettings Get() => settings;
}
sealed class SilentLogger : ITextLogger
{
    public void EmptyLine() { }
    public void Info(string message) { }
    public void Debug(string message) { }
    public void Error(string message) { }
    public void Warning(string message) { }
    public void InfoBlock(string title, string block) { }
    public void ErrorBlock(string title, string block) { }
}
