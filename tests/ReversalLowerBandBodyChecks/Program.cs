global using IbSwingTrader.Domain.Market;
using IbSwingTrader.Application.Candidates;
using System.Globalization;

var checks = 0;
void Check(bool condition, string reason)
{
    checks++;
    if (!condition) throw new InvalidOperationException(reason);
}
Candle Bar(DateTime time, decimal open, decimal close) => new()
{
    Time=time, Open=open, Close=close, Low=Math.Min(open,close)-1m,
    High=Math.Max(open,close)+1m, Volume=100
};
var now = new DateTime(2026,10,16,7,0,0);
var green = Bar(new(2026,10,15,16,0,0),100m,102m);
var red = Bar(green.Time,102m,100m);
string Describe(Candle bar, decimal lower) => ReversalLowerBandBodyDiagnostics.Describe([bar],[lower],Timeframe.H4,now);
Check(Describe(green,103m).Contains("LastLowerVsBody=AboveBody,"),"Green body top is Close");
Check(Describe(red,103m).Contains("LastLowerVsBody=AboveBody,"),"Red body top is Open");
Check(Describe(green,99m).Contains("LastLowerVsBody=BelowBody,"),"Lower below green body");
Check(Describe(red,99m).Contains("LastLowerVsBody=BelowBody,"),"Lower below red body");
Check(Describe(green,102m).Contains("LastLowerVsBody=InsideBody,"),"Body boundary touch is inside");
Check(Describe(green,0m).Contains("LastLowerVsBody=Unavailable,"),"Missing lower is not a signal");
Check(Describe(green,101m).Contains("BodyToPriorMedian=n/a"),"No baseline is explicit");
var prior = Bar(green.Time.AddHours(-4),100m,101m);
Check(ReversalLowerBandBodyDiagnostics.Describe([prior,green],[102m,101m],Timeframe.H4,now)
    .Contains("PositionChangedIntoBodyFromAbove=True"),"Record change of relative body position");
var original = ReversalLowerBandBodyDiagnostics.Describe([prior,green],[102m,101m],Timeframe.H4,now);
var partial = Bar(new(2026,10,16,4,0,0),100m,50m);
Check(original == ReversalLowerBandBodyDiagnostics.Describe([prior,green,partial],[102m,101m,999m],Timeframe.H4,now),
    "Incomplete H4 and its band do not affect closed diagnostics");
var d1 = Bar(new(2026,10,15),100m,101m);
var today = Bar(new(2026,10,16),100m,50m);
Check(ReversalLowerBandBodyDiagnostics.Describe([d1],[102m],Timeframe.D1,now) ==
    ReversalLowerBandBodyDiagnostics.Describe([d1,today],[102m,999m],Timeframe.D1,now),"Exclude incomplete D1");
Check(ReversalLowerBandBodyDiagnostics.Describe([green],[],Timeframe.H4,now).Contains("Status=InvalidInput"),"Aligned inputs required");
Check(ReversalLowerBandBodyDiagnostics.Describe([green,prior],[101m,102m],Timeframe.H4,now)
    .Contains("Status=InvalidCandles"),"Reject reversed times");
var impulse = Enumerable.Range(0,6).Select(i=>Bar(new DateTime(2026,10,14).AddHours(4*i),100m,i==5?90m:99m)).ToList();
var previousCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
    var result=ReversalLowerBandBodyDiagnostics.Describe(impulse,Enumerable.Repeat(95.5m,6).ToList(),Timeframe.H4,now);
    Check(result.Contains("BodyToPriorMedian=10, RangeToPriorMedian=4, ReferenceBars=5"),"Median excludes current impulse");
    Check(result.Contains("Lower=95.5,"),"Invariant numeric formatting");
}
finally { CultureInfo.CurrentCulture=previousCulture; }
Console.WriteLine($"Passed {checks} lower-band/body diagnostic checks.");
