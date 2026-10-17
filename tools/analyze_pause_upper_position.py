"""Cache-only prefix study of upper Bollinger position during post-rise pauses.

No production scanner/evaluation code, broker calls, or trade outcome simulation.
Fixed exploratory shape tolerances reuse the earlier Triangle history study.
"""
import argparse
import csv
import gzip
import json
import math
import random
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path
from statistics import mean, median

ROOT = Path(__file__).resolve().parents[1]
HORIZONS = (1, 3, 6, 12)
AGES = (1, 2, 3, 4)


def auc(pairs):
    pairs = sorted(pairs)
    positives = sum(y for _, y in pairs)
    negatives = len(pairs) - positives
    if not positives or not negatives:
        return None
    ranks, i = 0, 0
    while i < len(pairs):
        j = i + 1
        while j < len(pairs) and pairs[j][0] == pairs[i][0]:
            j += 1
        ranks += (i + 1 + j) / 2 * sum(y for _, y in pairs[i:j])
        i = j
    return (ranks - positives * (positives + 1) / 2) / (positives * negatives)


def bands(bars):
    result = [None] * len(bars)
    for i in range(19, len(bars)):
        values = [b['Close'] for b in bars[i-19:i+1]]
        mid = mean(values)
        sd = math.sqrt(mean((x - mid) ** 2 for x in values))
        result[i] = (mid + 2 * sd, mid, mid - 2 * sd)
    return result


def position(bar, upper):
    if upper < bar['Low']:
        return 'BelowLow'
    if upper > bar['High']:
        return 'AboveHigh'
    if upper < min(bar['Open'], bar['Close']):
        return 'LowerWick'
    if upper > max(bar['Open'], bar['Close']):
        return 'UpperWick'
    return 'Body'


def collect_segment(bars, ticker, tf):
    bb = bands(bars)
    rows = []
    # Non-overlap separately per age and rise type; decide exclusion using only
    # the detection index, never whether its forward outcome is interesting.
    blocked = defaultdict(lambda: -1)
    for t in range(25, len(bars)):
        for age in AGES:
            peak = t - age
            for rise_type, lengths in [('Impulse1', (1,)), ('Rise3or6', (3, 6))]:
                key = age, rise_type
                if t <= blocked[key]:
                    continue
                for ramp in lengths:
                    start = peak - ramp
                    if start < 19:
                        continue
                    gain = bars[peak]['Close'] - bars[start]['Close']
                    width0 = bb[start][0] - bb[start][2]
                    if gain < max(bars[start]['Close'] * .05, width0 * .75) or gain <= 0:
                        continue
                    if ramp == 1 and bars[peak]['Close'] <= bars[peak]['Open']:
                        continue
                    upward = sum(max(0, bars[j]['Close'] - bars[j-1]['Close'])
                                 for j in range(start+1, peak+1))
                    body = mean(abs(bars[j]['Close'] - bars[j]['Open']) for j in range(start+1, peak+1))
                    if upward <= 0 or gain / upward < .75 or body <= 0:
                        continue
                    tail = bars[peak+1:t+1]
                    closes = [b['Close'] for b in tail]
                    if (max(closes) - min(closes) > .45 * gain or
                        max(abs(c - bars[peak]['Close']) for c in closes) > .45 * gain or
                        median(abs(b['Close'] - b['Open']) for b in tail) > .40 * body):
                        continue
                    if bars[peak]['Volume'] <= 0 or not any(b['Volume'] > 0 for b in tail):
                        continue
                    u, m, l = bb[t]
                    if u <= l or bars[t]['Close'] <= m:
                        continue
                    p = position(bars[t], u)
                    group = p if p in ('BelowLow', 'AboveHigh') else 'InsideRange'
                    deltas = [bb[j][0] - bb[j-1][0] for j in range(t-2, t+1)]
                    pause = (deltas[0] > deltas[1] > 0 and deltas[2] < deltas[1]
                             and max(bars[t-2]['Close'], bars[t-1]['Close']) > bars[t-3]['Close']
                             and bars[t]['Close'] <= bars[t-1]['Close'] and m >= bb[t-1][1])
                    row = dict(ticker=ticker, timeframe=tf, time=bars[t]['Time'],
                               ramp_start=bars[start]['Time'], ramp_end=bars[peak]['Time'],
                               age=age, rise_type=rise_type, ramp_bars=ramp,
                               rise_pct=100 * gain / bars[start]['Close'],
                               position=p, group=group, upper=u, mid=m,
                               low=bars[t]['Low'], high=bars[t]['High'], close=bars[t]['Close'],
                               close_upper_width=(bars[t]['Close']-u)/(u-l),
                               low_upper_width=(bars[t]['Low']-u)/(u-l),
                               pause_diagnostic=pause,
                               positive_volume_fraction=mean(b['Volume'] > 0 for b in tail),
                               outcomes={})
                    for horizon in HORIZONS:
                        if t + horizon >= len(bars):
                            continue
                        future = bars[t+1:t+horizon+1]
                        change = future[-1]['Close'] - bars[t]['Close']
                        label = 'Down' if change <= -.25 * gain else 'Up' if change >= .25 * gain else 'Sideways'
                        row['outcomes'][str(horizon)] = dict(
                            label=label, end=future[-1]['Time'],
                            close_pct=100 * change / bars[t]['Close'],
                            next_open_pct=100 * (future[0]['Open']/bars[t]['Close']-1),
                            min_pct=100 * (min(b['Low'] for b in future)/bars[t]['Close']-1),
                            max_pct=100 * (max(b['High'] for b in future)/bars[t]['Close']-1))
                    rows.append(row)
                    blocked[key] = t + max(HORIZONS)
                    break
    return rows


def summarize(rows, horizon):
    selected = [(r, r['outcomes'][str(horizon)]) for r in rows if str(horizon) in r['outcomes']]
    if not selected:
        return dict(n=0, incomplete=len(rows))
    labels = Counter(o['label'] for _, o in selected)
    return dict(n=len(selected), incomplete=len(rows)-len(selected),
                tickers=len({r['ticker'] for r, _ in selected}), labels=dict(labels),
                down_pct=100*labels['Down']/len(selected), up_pct=100*labels['Up']/len(selected),
                median_close_pct=median(o['close_pct'] for _, o in selected),
                median_min_pct=median(o['min_pct'] for _, o in selected),
                median_max_pct=median(o['max_pct'] for _, o in selected),
                median_rise_pct=median(r['rise_pct'] for r, _ in selected),
                close_down_5pct=100*mean(o['close_pct'] <= -5 for _, o in selected),
                low_down_5pct=100*mean(o['min_pct'] <= -5 for _, o in selected),
                auc_low_gap_predicts_down=auc([(r['low_upper_width'], o['label']=='Down') for r, o in selected]),
                auc_gap_predicts_down=auc([(r['close_upper_width'], o['label']=='Down') for r, o in selected]))


def cluster_difference(rows, horizon=3):
    counts = defaultdict(lambda: [0, 0, 0, 0])
    for r in rows:
        o = r['outcomes'].get(str(horizon))
        if o is None:
            continue
        offset = 0 if r['group'] == 'BelowLow' else 2
        counts[r['ticker']][offset] += o['label'] == 'Down'
        counts[r['ticker']][offset+1] += 1
    entries = list(counts.values())
    if not entries:
        return None
    total = [sum(x[i] for x in entries) for i in range(4)]
    if min(total[1], total[3]) == 0:
        return None
    rng = random.Random(20261015)
    differences = []
    for _ in range(400):
        sample = rng.choices(entries, k=len(entries))
        sums = [sum(x[i] for x in sample) for i in range(4)]
        if sums[1] and sums[3]:
            differences.append(100*(sums[0]/sums[1]-sums[2]/sums[3]))
    differences.sort()
    return dict(down_rate_difference_pp=100*(total[0]/total[1]-total[2]/total[3]),
                ticker_bootstrap_95pct=[differences[int(.025*len(differences))], differences[int(.975*len(differences))]],
                bootstrap_repetitions=400)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--asof', default='2026-10-15', help='Exclude this date and later on all timeframes')
    parser.add_argument('--out', default=str(ROOT/'docs/analysis-2026-10-15-pause-upper-position.json'))
    parser.add_argument('--reuse-events', action='store_true', help='Rebuild summaries from saved events instead of rereading cache')
    args = parser.parse_args()
    events, examples = [], []
    coverage = defaultdict(Counter)
    if args.reuse_events:
        saved = json.loads(Path(args.out).read_text())
        if saved['asof_exclusive'] != args.asof:
            raise ValueError('Saved asof does not match requested asof')
        if 'events' in saved:
            events = saved['events']
        else:
            with gzip.open(Path(args.out).with_suffix('.events.json.gz'), 'rt') as f:
                events = json.load(f)
        examples = saved['examples']
        coverage.update({k:Counter(v) for k,v in saved['coverage'].items()})
    for index, path in enumerate([] if args.reuse_events else sorted((ROOT/'Data/cache').glob('*.json'))):
        if index % 250 == 0:
            print(f'Cache files processed: {index}', flush=True)
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        if not isinstance(data, dict) or not isinstance(data.get('Timeframes'), dict):
            continue
        for tf in ('H4', 'D1'):
            raw = data['Timeframes'].get(tf, [])
            source = sorted({b['Time']: b for b in raw if b['Time'][:10] < args.asof}.values(), key=lambda b:b['Time'])
            segments, current = [], []
            for b in source:
                valid = (all(math.isfinite(b[k]) and b[k] > 0 for k in ('Open','High','Low','Close'))
                         and b['Low'] <= min(b['Open'], b['Close']) <= max(b['Open'], b['Close']) <= b['High'])
                gap = current and (datetime.fromisoformat(b['Time'])-datetime.fromisoformat(current[-1]['Time'])).days > 7
                if not valid or gap:
                    if current:
                        segments.append(current)
                    current = []
                    coverage[tf]['invalid_bars' if not valid else 'gaps_over_7days'] += 1
                if valid:
                    current.append(b)
            if current:
                segments.append(current)
            usable = [s for s in segments if len(s) >= 26]
            if usable:
                coverage[tf]['tickers'] += 1
                coverage[tf]['bars'] += sum(len(s) for s in usable)
            for segment in usable:
                found = collect_segment(segment, path.stem, tf)
                events.extend(found)
                if path.stem in ('NVAX', 'ACVA', 'PENG'):
                    examples.extend(r for r in found if
                        (path.stem=='ACVA' and '2026-09-11' in r['time']) or
                        (path.stem=='NVAX' and '2026-10-06' in r['time']) or
                        (path.stem=='PENG' and '2026-10-13' in r['time']))
    summaries = []
    for tf in ('H4', 'D1'):
        for rise in ('Impulse1', 'Rise3or6'):
            for age in AGES:
                base = [r for r in events if r['timeframe']==tf and r['rise_type']==rise and r['age']==age]
                by_group = {group: {str(h):summarize([r for r in base if r['group']==group], h) for h in HORIZONS}
                            for group in ('BelowLow', 'InsideRange', 'AboveHigh')}
                summaries.append(dict(timeframe=tf, rise_type=rise, age=age,
                    all={str(h):summarize(base,h) for h in HORIZONS}, groups=by_group,
                    periods={period:summarize([r for r in base if
                        (r['time']<'2026-09-01' if period=='before_sep' else r['time']>='2026-09-01' if period=='sep_onward' else r['time']>='2026-10-01')],3)
                        for period in ('before_sep','sep_onward','october')},
                    period_groups={period:{group:summarize([r for r in base if r['group']==group and
                        (r['time']<'2026-09-01' if period=='before_sep' else r['time']>='2026-09-01' if period=='sep_onward' else r['time']>='2026-10-01')],3)
                        for group in ('BelowLow','InsideRange','AboveHigh')} for period in ('before_sep','sep_onward','october')},
                    diagnostic_groups={str(flag):{g:summarize([r for r in base if r['pause_diagnostic']==flag and r['group']==g],3)
                        for g in ('BelowLow','InsideRange','AboveHigh')} for flag in (False,True)},
                    cluster_difference=cluster_difference(base)))
    primary = [r for r in events if r['timeframe']=='H4' and r['rise_type']=='Impulse1' and r['age']==2]
    sensitivity = {}
    for name, selected in [('all',primary), ('price_at_least_5',[r for r in primary if r['close']>=5]),
                           ('all_pause_bars_positive_volume',[r for r in primary if r['positive_volume_fraction']==1]),
                           ('pause_diagnostic_only',[r for r in primary if r['pause_diagnostic']])]:
        sensitivity[name] = {g:summarize([r for r in selected if (r['group']=='BelowLow')==(g=='BelowLow')],3)
                             for g in ('BelowLow','ReachedOrPassedLow')}
    sensitivity['rise_size_strata'] = {f'{lo}-{hi}':{g:summarize([r for r in primary if lo<=r['rise_pct']<hi and
        (r['group']=='BelowLow')==(g=='BelowLow')],3) for g in ('BelowLow','ReachedOrPassedLow')}
        for lo,hi in ((5,10),(10,20),(20,50),(50,100000))}
    result = dict(asof_exclusive=args.asof, coverage={k:dict(v) for k,v in coverage.items()},
                  event_count=len(events), summaries=summaries, sensitivity=sensitivity, examples=examples)
    out = Path(args.out)
    with gzip.open(out.with_suffix('.events.json.gz'), 'wt') as f:
        json.dump(events, f, separators=(',',':'))
    out.write_text(json.dumps(result, indent=2, ensure_ascii=False)+'\n')
    with gzip.open(out.with_suffix('.csv.gz'), 'wt', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=['ticker','timeframe','time','rise_type','age','group','position',
            'upper','low','high','close','pause_diagnostic','horizon','label','close_pct','min_pct','max_pct'])
        writer.writeheader()
        for r in events:
            for h,o in r['outcomes'].items():
                writer.writerow({**{k:r[k] for k in writer.fieldnames if k in r},'horizon':h,
                    **{k:o[k] for k in ('label','close_pct','min_pct','max_pct')}})
    print(json.dumps({k:v for k,v in result.items() if k not in ('examples','summaries','sensitivity')}, indent=2))
    for s in summaries:
        print(s['timeframe'],s['rise_type'],'age',s['age'],
              {g:{k:v for k,v in s['groups'][g]['3'].items() if k in ('n','down_pct','up_pct','median_close_pct')} for g in s['groups']},
              'difference',s['cluster_difference'],flush=True)


if __name__ == '__main__':
    main()
