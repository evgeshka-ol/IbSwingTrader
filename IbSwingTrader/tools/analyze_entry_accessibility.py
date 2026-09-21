#!/usr/bin/env python3
"""Paired entry-touch replay; exits, stops and P&L are deliberately ignored."""
import argparse
import csv
import json
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path


def dt(value):
    return datetime.fromisoformat(value)


def aggregate(rows, positive_volume=False):
    key = '_volume' if positive_volume else ''
    pair = Counter()
    for r in rows:
        old, new = r['old_hit'+key], r['formula_hit'+key]
        pair['both' if old and new else 'rescued' if new else 'lost' if old else 'neither'] += 1
    return dict(n=len(rows), old_entries=sum(r['old_hit'+key] for r in rows),
                formula_entries=sum(r['formula_hit'+key] for r in rows),
                open_baseline_entries=sum(r['open_hit'+key] for r in rows),
                **{k:pair[k] for k in ('both','rescued','lost','neither')},
                net_gain=pair['rescued']-pair['lost'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--replay', type=Path, default=Path('/tmp/ibswingtrader-execution-log-study/replay.csv'))
    parser.add_argument('--data', type=Path, default=Path(__file__).resolve().parents[2]/'Data')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    latest = {}
    with args.replay.open(newline='') as f:
        for r in csv.DictReader(f):
            key = (r['ticker'],r['scan'][:10])
            if key not in latest or r['scan'] > latest[key]['scan']:
                latest[key] = r
    evaluations = defaultdict(list)
    with (args.data/'datasets/evaluation-dataset.csv').open(encoding='utf-8-sig',newline='') as f:
        for r in csv.DictReader(f):
            evaluations[(r['Ticker'],r['ScanTime'],r['PresetScanCode'])].append(r)
    by_ticker = defaultdict(list)
    for row in latest.values():
        if row.get('forecast') and Decimal(row['saved_entry']) > 0:
            by_ticker[row['ticker']].append(row)
    results = []
    for ticker, rows in sorted(by_ticker.items()):
        payload = json.loads((args.data/'cache'/f'{ticker}.json').read_text(encoding='utf-8-sig'), parse_float=Decimal)
        bars = {dt(b['Time']):b for b in payload['Timeframes'].get('M5',[])}
        for row in rows:
            at = dt(row['reference_time'])
            old = Decimal(row['saved_entry'])
            opening = Decimal(row['current_open'])
            new = (opening+Decimal(row['previous_close'])-Decimal(row['previous_open'])).quantize(Decimal('.01'),rounding=ROUND_HALF_UP)
            matched = evaluations.get((ticker,row['scan'],row['preset']),[])
            outcome = matched[0]['Outcome'] if len(matched)==1 else 'Missing' if not matched else 'Ambiguous'
            for hours in (1,24):
                end = at+timedelta(hours=hours)
                future = [(t,b) for t,b in sorted(bars.items()) if at <= t and t+timedelta(minutes=5) <= end and min(b['Low'],b['High'])>0]
                if not future:
                    results.append(dict(ticker=ticker,scan=row['scan'],hours=hours,status='NoFutureData'))
                    continue
                # A strict standard extended-hours grid is a coverage diagnostic,
                # not a claim that a missing bar means no trade occurred.
                grid = []
                t = at.replace(minute=at.minute//5*5,second=0,microsecond=0)
                if t < at:
                    t += timedelta(minutes=5)
                while t+timedelta(minutes=5) <= end:
                    if t.weekday()<5 and 4<=t.hour<20:
                        grid.append(t)
                    t += timedelta(minutes=5)
                valid_times = {t for t,_ in future}
                coverage = sum(t in valid_times for t in grid)/len(grid) if grid else None
                r = dict(ticker=ticker,scan=row['scan'],hours=hours,status='Observed',
                         published=at.isoformat(),old_entry=str(old),formula_entry=str(new),open_entry=str(opening),
                         saved_outcome=outcome,bars=len(future),expected_grid_bars=len(grid),coverage=coverage,
                         feature_positive_volume=float(row['previous_volume'])>0 and float(row['current_volume'])>0)
                for name,price in [('old',old),('formula',new),('open',opening)]:
                    for suffix,require_volume in [('',False),('_volume',True)]:
                        touched = [(t,b) for t,b in future if b['Low']<=price<=b['High'] and (not require_volume or b.get('Volume',0)>0)]
                        r[name+'_hit'+suffix]=bool(touched)
                        r[name+'_first'+suffix]=touched[0][0].isoformat() if touched else None
                results.append(r)
    report = dict(selected_latest_ticker_days=len(latest), eligible_formula_cases=sum(len(x) for x in by_ticker.values()), horizons={})
    for hours in (1,24):
        rows = [r for r in results if r['hours']==hours and r['status']=='Observed']
        full = [r for r in rows if r['coverage']==1]
        liquid = [r for r in rows if r['feature_positive_volume']]
        report['horizons'][hours] = dict(
            no_future_data=sum(r['hours']==hours and r['status']=='NoFutureData' for r in results),
            observed=aggregate(rows), full_grid=aggregate(full),
            positive_volume=aggregate(liquid,True),
            full_grid_positive_volume=aggregate([r for r in full if r['feature_positive_volume']],True),
            coverage_at_least_90pct=aggregate([r for r in rows if r['coverage'] is not None and r['coverage']>=.9]),
            saved_outcomes=dict(Counter(r['saved_outcome'] for r in rows)),
            by_saved_outcome={o:aggregate([r for r in rows if r['saved_outcome']==o]) for o in sorted(set(r['saved_outcome'] for r in rows))},
            by_month={m:aggregate([r for r in rows if r['scan'][:7]==m]) for m in sorted(set(r['scan'][:7] for r in rows))},
            changed_examples=[r for r in rows if r['old_hit']!=r['formula_hit']][:12])
    report['method'] = [
        'Latest admitted BellUp snapshot per ticker/day selected before checking M5 availability.',
        'Same future M5 bars for all prices; low <= entry <= high reproduces evaluator touch logic.',
        'Bars straddling publication or horizon end excluded to prevent pre-publication or beyond-horizon touches.',
        'Open here would mean entry touched with exits disabled; it is not the original evaluator Open outcome.',
        'No-touch in incomplete cache means not observed, not a proven NoEntry.',
        'Full grid means all standard Mon-Fri 04:00-20:00 five-minute slots exist; exchange holidays/halts may reduce coverage.',
        'Current-open baseline is observable from cached OHLC, not a reconstruction of the exact publication quote.',
        'No spread/order-book fill guarantee; exit, stop and profit are not evaluated.',
    ]
    args.output.mkdir(parents=True,exist_ok=True)
    fields=list(dict.fromkeys(k for row in results for k in row))
    with (args.output/'entries.csv').open('w',newline='',encoding='utf-8') as f:
        w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows(results)
    (args.output/'summary.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='horizons'},indent=2))
    for h,r in report['horizons'].items():
        print(h,json.dumps({k:v for k,v in r.items() if k!='changed_examples'},indent=2))


if __name__=='__main__':
    main()
