using System.Reflection;
using System.Text.Json;
using System.Diagnostics;
using IbSwingTrader.Abstractions.Dataset;
using IbSwingTrader.Abstractions.Logging;
using IbSwingTrader.Abstractions.Settings;
using IbSwingTrader.Application.Dataset;
using IbSwingTrader.Domain.Dataset;
using IbSwingTrader.Domain.Market;
using IbSwingTrader.Domain.Settings;
using IbSwingTrader.Infrastructure.Historical;

// Offline, user-run comparison against the pre-memoization snapshot algorithm.
// Pass a frozen fixture path, or run from the repository root with the default.
var path = args.FirstOrDefault() ?? "docs/artifacts/bellup-rank-empty-2026-11-17/h4-fixtures.json";
var fixtures = JsonSerializer.Deserialize<Dictionary<string, List<Candle>>>(File.ReadAllText(path))
    ?? throw new InvalidOperationException("Missing frozen H4 fixtures");
var folder = Path.Combine(Path.GetTempPath(), "ibswing-snapshot-" + Guid.NewGuid().ToString("N"));
var counter = new CountingEngine();
var cache = new HistoricalCache(new Paths(folder), counter, new Logger());
var method = typeof(HistoricalCache).GetMethod("BuildPatternSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("Snapshot method missing");
var checks = 0;
try
{
    foreach (var (symbol, bars) in fixtures)
    {
        Compare(symbol, bars);
        Compare(symbol + " short", bars.Take(5).ToList());
        Compare(symbol + " seed", bars.Take(20).ToList());
        // Same length/list identity changes must not reuse a previous snapshot's features.
        var changed = JsonSerializer.Deserialize<List<Candle>>(JsonSerializer.Serialize(bars))!;
        Compare(symbol + " before mutation", changed);
        changed[^1].Close += 10m;
        changed[^1].High = Math.Max(changed[^1].High, changed[^1].Close);
        Compare(symbol + " changed", changed);
    }
    Compare("empty", []);
    Console.WriteLine($"Historical snapshot checks passed: {checks}. No broker requests or source-cache writes.");
}
finally
{
    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
}

void Compare(string name, List<Candle> bars)
{
    var before = JsonSerializer.Serialize(bars);
    var timer = Stopwatch.StartNew();
    var legacy = Reference(bars);
    var legacyElapsed = timer.Elapsed;
    counter.Calls.Clear();
    timer.Restart();
    var actual = (CachedPatternSnapshot)method.Invoke(cache, [bars])!;
    var memoizedElapsed = timer.Elapsed;
    if (JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(legacy))
        throw new InvalidOperationException($"{name}: snapshot differs from uncached calculation");
    if (before != JsonSerializer.Serialize(bars))
        throw new InvalidOperationException($"{name}: mutated candles");
    var expected = Indexes(bars, "Daily").Concat(Indexes(bars, "Weekly")).Concat(Indexes(bars, "H4"))
        .Select(i => i + 1).Distinct().Order().ToArray();
    if (!expected.SequenceEqual(counter.Calls.Keys.Order()) || counter.Calls.Values.Any(n => n != 1))
        throw new InvalidOperationException($"{name}: feature prefixes were not calculated exactly once");
    checks++;
    Console.WriteLine($"{name}: all 21 series equal; {counter.Calls.Count} distinct feature calculations; " +
        $"legacy={legacyElapsed.TotalMilliseconds:F1}ms, memoized={memoizedElapsed.TotalMilliseconds:F1}ms");
}

static List<int> Indexes(List<Candle> bars, string frame)
{
    var seen = new HashSet<DateTime>();
    var indices = new List<int>();
    var limit = frame == "Daily" ? RecentSeriesWindow.Daily : frame == "Weekly" ? RecentSeriesWindow.Weekly : RecentSeriesWindow.H4;
    for (var i = bars.Count - 1; i >= 0; i--)
    {
        var t = bars[i].Time;
        var bucket = frame == "Daily" ? t.Date : frame == "Weekly"
            ? t.Date.AddDays(-((7 + (int)t.DayOfWeek - (int)DayOfWeek.Monday) % 7)) : t;
        if (!seen.Add(bucket)) continue;
        indices.Add(i);
        if (indices.Count == limit) break;
    }
    indices.Reverse();
    return indices;
}

static CachedPatternSnapshot Reference(List<Candle> bars)
{
    // Legacy: Calculate is called afresh for each point of each field, including
    // nullable Weekly values; the original rounding and sample selection remain.
    var result = new CachedPatternSnapshot { BollingerModel = FeatureEngine.BollingerModel };
    var engine = new FeatureEngine();
    foreach (var property in typeof(CachedPatternSnapshot).GetProperties().Where(p => p.PropertyType == typeof(List<decimal>)))
    {
        var frame = property.Name.StartsWith("RecentDaily") ? "Daily" : property.Name.StartsWith("RecentWeekly") ? "Weekly" : "H4";
        var suffix = property.Name[("Recent" + frame).Length..^"Series".Length];
        var featureName = suffix switch
        {
            "BbUpperBand" => "BollingerUpperBand", "BbMidBand" => "BollingerMidBand",
            "BbLowerBand" => "BollingerLowerBand", "Rsi" => "RSI14",
            "MacdLine" => "MACDLine", "MacdSignal" => "MACDSignal", "MacdHistogram" => "MACDHistogram",
            _ => throw new InvalidOperationException(suffix)
        };
        var prefix = frame == "H4" && !featureName.StartsWith("Bollinger") ? "" : frame;
        var feature = typeof(FeatureSet).GetProperty(prefix + featureName) ?? throw new InvalidOperationException(prefix + featureName);
        var values = new List<decimal>();
        foreach (var i in Indexes(bars, frame))
        {
            var value = (decimal?)feature.GetValue(engine.Calculate(bars, i + 1));
            if (value.HasValue) values.Add(decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero));
        }
        property.SetValue(result, values);
    }
    return result;
}

sealed class CountingEngine : IFeatureEngine
{
    readonly FeatureEngine engine = new();
    public Dictionary<int, int> Calls { get; } = new();
    public FeatureSet Calculate(List<Candle> candles, int index)
    {
        Calls[index] = Calls.GetValueOrDefault(index) + 1;
        return engine.Calculate(candles, index);
    }
    public FeatureSet CalculateLast(List<Candle> candles) => Calculate(candles, candles.Count);
}

sealed class Paths(string folder) : IAgentPathService
{
    public string GetCacheFolder() => folder;
    public string GetDataRoot() => throw new NotSupportedException();
    public string GetTradesFile() => throw new NotSupportedException();
    public string GetDatasetFile() => throw new NotSupportedException();
    public string GetCandidatesFile() => throw new NotSupportedException();
    public string GetEvaluationsFile() => throw new NotSupportedException();
    public string GetEvaluationsArchiveFile() => throw new NotSupportedException();
    public string GetWishListFile() => throw new NotSupportedException();
    public string GetProcessedCandidateFilesManifest() => throw new NotSupportedException();
    public string GetLogsFolder() => throw new NotSupportedException();
}

sealed class Logger : ITextLogger
{
    public void EmptyLine() { }
    public void Info(string message) { }
    public void Debug(string message) { }
    public void Error(string message) => throw new InvalidOperationException(message);
    public void InfoBlock(string title, string block) { }
    public void ErrorBlock(string title, string block) => throw new InvalidOperationException(title + block);
    public void Warning(string message) { }
}
