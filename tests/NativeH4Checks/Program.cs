using IbSwingTrader.Common.Time;
using IbSwingTrader.Domain.Market;
using IbSwingTrader.Application.Candidates;
using System.Text.Json;

var checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
List<Candle> Day(DateTime date, params int[] hours) => hours.Select(h=>new Candle {
    Time=date.AddHours(h), Open=10m,High=11m,Low=9m,Close=10m,Volume=100m }).ToList();
var summer = new DateTime(2026,10,30);
var winter = new DateTime(2026,11,2);
var summerBars = Day(summer,4,8,12,16);
var winterBars = Day(winter,4,7,11,15,19);
var input = summerBars.Concat(winterBars).ToList();
var before = input.Select(x=>(x.Time,x.Close)).ToList();
var clock = NativeH4Clock.From(input, winter.AddHours(20));
Check(clock.Starts(summer).SequenceEqual(summerBars.Select(x=>x.Time)), "Observed summer grid stays04/08/12/16");
Check(clock.Starts(winter).SequenceEqual(winterBars.Select(x=>x.Time)), "The same observed UTC phase projects winter04/07/11/15/19");
Check(clock.End(winter.AddHours(4))==winter.AddHours(7), "Winter opening stub ends07, not08");
Check(clock.End(winter.AddHours(19))==winter.AddHours(20), "Winter trailing stub ends20, not23");
Check(clock.CurrentStart(winter.AddHours(17.5))==winter.AddHours(15), "Evening current H4 starts15, not16");
Check(clock.CurrentStart(winter.AddHours(19.5))==winter.AddHours(19), "Late afterhours current H4 starts19");
Check(clock.CurrentStart(winter.AddHours(20))==null && clock.CurrentStart(winter.AddHours(3.99))==null,
    "Outside the actual extended session there is no current bar");
Check(BellUpEntryTiming.GetCompletedCandles(winterBars,Timeframe.H4,winter.AddHours(6.99)).Count==0,
    "Future final opening-stub values are excluded before07");
Check(BellUpEntryTiming.GetCompletedCandles(winterBars,Timeframe.H4,winter.AddHours(7)).Count==1,
    "Opening stub is completed exactly at07");
Check(BellUpEntryTiming.GetCompletedCandles(winterBars,Timeframe.H4,winter.AddHours(19.99)).Count==4 &&
    BellUpEntryTiming.GetCompletedCandles(winterBars,Timeframe.H4,winter.AddHours(20)).Count==5,
    "Trailing stub changes completion only at20");
Check(clock.LastClosedStart([summer,winter],winter.AddHours(4.25))==summer.AddHours(16),
    "Monday morning correctly expects the last summer Friday bar");
Check(clock.LastClosedStart([summer,winter],winter.AddDays(1).AddHours(4.25))==winter.AddHours(19),
    "Tuesday morning expects winter19 rather than summer16");
var alternate = NativeH4Clock.From(Day(winter,4,8,12,16),winter.AddHours(18));
Check(alternate.CurrentStart(winter.AddHours(17.5))==winter.AddHours(16) && alternate.End(winter.AddHours(4))==winter.AddHours(8),
    "Observed alternative broker phase is respected instead of shifting all winter timestamps");
var mixedPhase = NativeH4Clock.From(summerBars.Concat(Day(winter,4,8,12,16)),winter.AddHours(18));
Check(mixedPhase.Starts(summer).SequenceEqual(summerBars.Select(x=>x.Time)) &&
    mixedPhase.CurrentStart(winter.AddHours(17.5))==winter.AddHours(16),
    "Actual phase per historical date takes precedence over a single global projection");
Check(before.SequenceEqual(input.Select(x=>(x.Time,x.Close))), "Native timestamps and OHLC are never rewritten");
var exportMethod = typeof(CandidateFinder).GetMethod("BuildRecentH4CandleSeries",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var exported = (List<decimal>)exportMethod.Invoke(null,
    [winterBars, winterBars.Count-1, (Func<Candle,decimal>)(bar=>bar.Close)])!;
Check(exported.Count == 5, "Native04:00 and07:00 bars remain separate in scanner H4 exports");
var halfDay = new DateTime(2026,11,27);
var early = NativeH4Clock.From(input, winter.AddHours(20), new Dictionary<DateTime,DateTime> {
    [halfDay]=halfDay.AddHours(13), [halfDay.AddDays(-2)]=halfDay.AddDays(-2).AddHours(20) });
Check(early.Starts(halfDay).SequenceEqual(new[]{halfDay.AddHours(4),halfDay.AddHours(7),halfDay.AddHours(11)}) &&
    early.End(halfDay.AddHours(11))==halfDay.AddHours(13) && early.CurrentStart(halfDay.AddHours(13))==null,
    "Configured early close caps the native interval and forming window");
Check(BellUpEntryTiming.GetCompletedCandles(Day(halfDay,4,7,11,15,19),Timeframe.H4,
    halfDay.AddHours(13),early).Count==3 && early.End(halfDay.AddHours(15))==DateTime.MaxValue,
    "A cached bar outside an early-close session cannot become completed before its own start");
Check(early.LastClosedStart(early.TradingDates,halfDay.AddHours(3))==halfDay.AddDays(-2).AddHours(19),
    "Expected previous close respects the provided holiday trading dates");
var earlyHistory = Day(halfDay.AddDays(-2),7,11,15,19).Concat(Day(halfDay,4,7,11)).ToList();
earlyHistory[^1].Close=20m; earlyHistory[^1].High=20m;
Check(!BellUpEntryTiming.IsReady([],earlyHistory,Timeframe.H4,halfDay.AddHours(13),out _,out var earlyReason,
    h4Clock:early) && earlyReason=="Recent boost",
    "Closed-only timing also retains the calendar-shortened final bar when there is no current observation");
var touch = new ReversalUpperTouch(0,11m,10m,"CompletedHigh");
Check(ReversalImpulseCompletion.FromClosedBar("H4",winter.AddHours(15),winter.AddHours(19),touch,winter.AddHours(20),clock)?.ConfirmedAt==winter.AddHours(20),
    "Historical reversal completion confirms the trailing stub at20");
Check(ReversalImpulseCompletion.FromClosedBar("H4",winter.AddHours(15),winter.AddHours(19),touch,winter.AddHours(19.99),clock)==null,
    "A forming trailing-stub high does not consume an episode early");
using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","sofi-2026-11-03-h4-prefix.json")));
var at=fixture.RootElement.GetProperty("DecisionAt").GetDateTime();
var native=JsonSerializer.Deserialize<List<Candle>>(fixture.RootElement.GetProperty("H4"))!;
var m5=JsonSerializer.Deserialize<List<Candle>>(fixture.RootElement.GetProperty("M5"))!;
var actual=NativeH4Clock.From(native,at);
var current=BellUpEntryTiming.BuildCurrentFromM5(m5,Timeframe.H4,at,actual);
Check(current!=null && current.Bar.Time==at.Date.AddHours(15), "Actual saved November3 M5 builds the native15:00 current H4");
var expected=actual.LastClosedStart([winter,at.Date],at);
Check(expected==at.Date.AddHours(11) && native[^1].Time==expected && actual.End(native[^1].Time)==current!.Bar.Time,
    "Actual native closed11:00 and forming15:00 are adjacent; no artificial Missing candles gap");
var quote = new ExecutionPriceSnapshot(current!.Bar.Close,at.AddSeconds(-1),at,"IBLiveAskObserved",null,null);
Check(ReversalH4Momentum.Evaluate(native,quote,at,expected,actual).IsAvailable,
    "Actual winter prefix provides available fresh H4 momentum, independent of improvement direction");
Check(!ReversalH4Momentum.Evaluate(native,quote,at,at.Date.AddHours(12),actual).IsAvailable,
    "A genuinely missing expected native bar still rejects fresh momentum");
Check(BellUpEntryTiming.CurrentStart(Timeframe.H4,at,actual)==at.Date.AddHours(15),
    "Current-aware BellUp timing uses the same native clock");
Console.WriteLine($"Passed {checks} native H4 checks.");
