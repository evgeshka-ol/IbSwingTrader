"""Offline exploratory screen. No broker calls or production rule changes.

Saved rounded scanner series are primary. Cache replay is retrospective, and
uses only fully elapsed M5 bars; it cannot prove live broker availability.
"""
import csv
import json
import math
import re
import statistics
from collections import Counter
from datetime import datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def dt(s):
    return datetime.fromisoformat(s).replace(tzinfo=None)


def series(s):
    return [float(x) for x in s.strip('[]').split()] if s else []


def slope(a):
    return round((a[-1] / a[0] - 1) * 100, 2) if a and a[0] else 0


def signals(c, u, m, l, h, r):
    n = min(map(len, (c, u, m, l, h)))
    if n < 7:
        return {}
    c, u, m, l, h = [a[-n:] for a in (c, u, m, l, h)]
    delta = lambda a: [y-x for x, y in zip(a, a[1:])]
    ld, md, hd = delta(l), delta(m), delta(h)
    emerging = ld[-1] < 0 and ld[-1] > ld[-2] > ld[-3]
    turning = ld[-1] >= 0 and ld[-2] < 0 and ld[-2] > ld[-3]
    strong = sum(x >= 0 for x in ld[-3:]) >= 2 and ld[-1] >= 0 and slope(l[-3:]) >= .8
    decel = slope(m[-3:]) >= 0 or (slope(m[-6:-3]) < 0 and slope(m[-3:]) < 0 and abs(slope(m[-3:])) <= abs(slope(m[-6:-3])) * .65)
    hooked = md[-1] >= 0 or md[-1] > min(md[:-2][-5:]) or md[-1] >= md[-2]
    distances = [b-a for a, b in zip(c, m) if b-a > 0]
    price = c[-1] < m[-1] and (c[-1] >= c[-2] or c[-1] > min(c[:-1][-4:])) and len(distances) >= 3 and distances[-1] < min(distances[-2], distances[-3])
    core = any(x < 0 for x in ld[-5:]) and (strong or emerging or turning) and any(x < 0 for x in ld[-3:-1]) and hooked and (decel or turning) and price and (sum(x > 0 for x in hd[-3:]) >= 2 and h[-1] > h[-3] or h[-1] > h[-2] >= h[-3] or emerging)
    compression = u[-1]-l[-1] < u[-4]-l[-4]
    rsi = len(r) < 4 or r[-1] > r[-2] and r[-1] > min(r[-4:])
    bend = any(all(x < 0 for x in md[i-4:i-1]) and md[i-1] > md[i-2] and md[i-1] >= statistics.mean(md[i-4:i-1]) * .65 and c[i-1] < m[i-1] and c[i] > c[i-1] and h[i] > h[i-1] for i in range(max(4, n-6), n))
    # Exploratory multi-bar turn, deliberately no fitted thresholds.
    gradual = all(x < 0 for x in md[-4:]) and md[-1] > md[-4] and sum(y > x for x, y in zip(md[-4:], md[-3:])) >= 2 and c[-1] > c[-3] and h[-1] > h[-2] >= h[-3] and min(c[-4:]) < m[-4]
    return dict(strict=bool(core and compression and rsi), without_rsi=bool(core and compression), without_compression=bool(core and rsi), without_both=bool(core), bend65=bend, gradual=gradual, compression=compression, rsi=rsi)


def indicators(bars):
    c = [b['Close'] for b in bars]
    u, m, l, h, r = [], [], [], [], []
    fast = slow = c[0]
    signal = 0
    for i, value in enumerate(c):
        fast += (value-fast)*2/13
        slow += (value-slow)*2/27
        macd = fast-slow
        signal += (macd-signal)*.2
        h.append(macd-signal)
        if i >= 19:
            window = c[i-19:i+1]
            mid = sum(window)/20
            width = 2*math.sqrt(sum((x-mid)**2 for x in window)/20)
            u.append(mid+width); m.append(mid); l.append(mid-width)
        if i >= 14:
            changes = [y-x for x, y in zip(c[i-14:i], c[i-13:i+1])]
            gain = sum(max(x, 0) for x in changes)
            loss = sum(max(-x, 0) for x in changes)
            r.append(100 if loss == 0 else 100-100/(1+gain/loss))
    return signals(c, u, m, l, h, r), m[-1] if m else None


def comparisons(results):
    output = {}
    for variant in ('without_rsi', 'without_compression', 'without_both', 'gradual'):
        baseline = 'bend65' if variant == 'gradual' else 'strict'
        rows = [x for x in results if x.get('cache_snapshot_aligned') and x['cache_closed'].get(variant) and not x['cache_closed'].get(baseline)]
        observed = [x for x in rows if 'future' in x]
        output[variant] = dict(baseline=baseline, added_rows=len(rows), observed_rows=len(observed), hit5=sum(x['future']['hit5'] for x in observed), hit10=sum(x['future']['up_pct'] >= 10 for x in observed))
    output['partial_bend_added'] = [dict(ticker=x['ticker'], scan=x['scan'], future=x.get('future')) for x in results if x['partial']['status'] == 'usable' and x['partial']['signals'].get('bend65') and not x.get('cache_closed', {}).get('bend65')]
    return output


def main():
    logs = {}
    for path in sorted((ROOT/'Data/logs').glob('log-202610*.log')):
        for line in path.read_text(encoding='utf-8-sig', errors='replace').splitlines():
            match = re.search(r'Reversal (?:episode|phase diagnosis): ([A-Z0-9.]+)\.', line)
            if match:
                logs.setdefault(match[1], []).append((dt(line.split()[0]), line))
    rows = list(csv.DictReader((ROOT/'Data/Tickers/candidates.csv').open(encoding='utf-8-sig')))
    results = []
    caches = {}
    for row in rows:
        if row['ScanTime'] < '2026-10-01' or row['CandidateGroup'] == 'BellUp' or row['PatternVerdictReason'].startswith('BellUp confirmed'):
            continue
        ticker = row['Ticker']; publication = dt(row['PublishedAt'] or row['ScanTime'])
        start = dt(row['RunStartedAt'] or row['ScanTime'])
        decisions = [(t, line) for t, line in logs.get(ticker, []) if start <= t <= publication]
        decision, line = decisions[-1] if decisions else (dt(row['SignalObservedAt'] or row['ScanTime']), '')
        item = dict(ticker=ticker, scan=row['ScanTime'], reason=row['PatternVerdictReason'], decision=str(decision), decision_source='log' if decisions else 'snapshot_observed_or_scan', bridge_unavailable='PriceSource=HistoricalSnapshot' in line)
        item['saved'] = {}
        for frame in ('H4', 'Daily'):
            cols = ['Close', 'BbUpperBand', 'BbMidBand', 'BbLowerBand', 'MacdHistogram', 'Rsi']
            item['saved'][frame] = signals(*(series(row.get(f'Recent{frame}{col}Series', '')) for col in cols))
        if ticker not in caches:
            path = ROOT/f'Data/cache/{ticker}.json'
            caches[ticker] = json.loads(path.read_text()).get('Timeframes', {}) if path.exists() else {}
        cache = caches[ticker]
        closed = sorted((b for b in cache.get('H4', []) if dt(b['Time'])+timedelta(hours=4) <= decision), key=lambda b:b['Time'])
        current_start = decision.replace(hour=(decision.hour//4)*4, minute=0, second=0, microsecond=0)
        m5 = sorted((b for b in cache.get('M5', []) if current_start <= dt(b['Time']) and dt(b['Time'])+timedelta(minutes=5) <= decision), key=lambda b:b['Time'])
        observed = [b for b in m5 if b['Volume'] > 0]
        item['partial'] = dict(bars=len(m5), observed_bars=len(observed), start=str(current_start), last=m5[-1]['Time'] if m5 else None, status='no_observed_m5')
        if len(closed) >= 40:
            item['cache_closed'], mid = indicators(closed)
            saved_mid = series(row.get('RecentH4BbMidBandSeries', ''))
            item['closed_mid_difference'] = mid-saved_mid[-1] if saved_mid else None
            saved_close = series(row.get('RecentH4CloseSeries', ''))
            item['cache_snapshot_aligned'] = bool(saved_mid and saved_close and abs(mid-saved_mid[-1]) <= .006 and abs(closed[-1]['Close']-saved_close[-1]) <= .006)
            if observed:
                last = m5[-1]
                gaps = sum(dt(y['Time'])-dt(x['Time']) > timedelta(minutes=5) for x,y in zip(m5,m5[1:]))
                stale = (decision-dt(last['Time'])-timedelta(minutes=5)).total_seconds()/60
                full_start = dt(m5[0]['Time']) == current_start
                item['partial'].update(status='usable' if not gaps and stale <= 10 and full_start else 'incomplete', gaps=gaps, stale_minutes=stale, covers_start=full_start)
                partial = dict(Open=m5[0]['Open'], High=max(b['High'] for b in m5), Low=min(b['Low'] for b in m5), Close=last['Close'])
                item['partial']['ohlc'] = partial
                item['partial']['signals'], item['partial']['mid'] = indicators(closed+[partial])
        # Outcome screen: observed post-publication bars, at most five trading dates.
        future = sorted((b for b in cache.get('M5', []) if publication <= dt(b['Time']) < publication+timedelta(days=10) and b['Volume'] > 0), key=lambda b:b['Time'])
        dates = sorted(set(b['Time'][:10] for b in future))[:5]
        future = [b for b in future if b['Time'][:10] in dates]
        if future and dt(future[0]['Time'])-publication <= timedelta(hours=24):
            ref = future[0]['Open']; hit = next((i for i,b in enumerate(future) if b['High'] >= ref*1.05), None)
            item['future'] = dict(reference_open=ref, first=future[0]['Time'], last=future[-1]['Time'], trading_dates=len(dates), up_pct=(max(b['High'] for b in future)/ref-1)*100, down_pct=(min(b['Low'] for b in future)/ref-1)*100, hit5=hit is not None, adverse_before_hit5_pct=(min(b['Low'] for b in future[:hit+1])/ref-1)*100 if hit is not None else None)
        results.append(item)
        if len(results) % 100 == 0:
            print(f'Processed {len(results)} research rows', flush=True)
    variants = ['strict','without_rsi','without_compression','without_both','bend65','gradual']
    summary = {}
    for variant in variants:
        chosen = [x for x in results if x.get('cache_snapshot_aligned') and x.get('cache_closed', {}).get(variant)]
        outcomes = [x for x in chosen if 'future' in x]
        summary[variant] = dict(rows=len(chosen), tickers=len(set(x['ticker'] for x in chosen)), outcome_rows=len(outcomes), hit5=sum(x['future']['hit5'] for x in outcomes), hit10=sum(x['future']['up_pct'] >= 10 for x in outcomes), five_dates=sum(x['future']['trading_dates'] >= 5 for x in outcomes), median_up_pct=statistics.median(x['future']['up_pct'] for x in outcomes) if outcomes else None, median_down_pct=statistics.median(x['future']['down_pct'] for x in outcomes) if outcomes else None)
    output = dict(method='H4-only latest strict-gate ablation using native cache indicators where final closed price/mid reproduce saved rounded snapshot within .006. Saved rounded flags are diagnostic only. Cache replay retrospective; outcomes movement not trade wins. Repeated scans correlated; incomplete horizons censored.', rows=len(results), aligned_h4_rows=sum(x.get('cache_snapshot_aligned',False) for x in results), partial_status=dict(Counter(x['partial']['status'] for x in results)), summary=summary, cases=results)
    output['comparisons'] = comparisons(results)
    path = ROOT/'docs/research-2026-10-10-reversal-recognition.json'
    path.write_text(json.dumps(output, ensure_ascii=False, indent=2)+'\n')
    print(json.dumps({k:v for k,v in output.items() if k != 'cases'}, indent=2))
    for x in results:
        if x['ticker'] in ('DNA','INTR','STNE','PCVX','TJGC','GLUE'):
            print(json.dumps(x, ensure_ascii=False))


if __name__ == '__main__':
    main()
