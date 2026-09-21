#!/usr/bin/env python3
"""Join scanner logs, saved candidates and cached M5; never request broker data."""
import argparse
import csv
import json
import re
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from pathlib import Path
from statistics import mean, median


def dt(value):
    return datetime.fromisoformat(value) if value else None


def scan_runs(logs, counts):
    runs = []
    for path in sorted(logs.glob('log-*.log')):
        run = None
        scanner_file = False
        evaluation_file = False
        with path.open(encoding='utf-8-sig', errors='replace') as handle:
            for line in handle:
                try:
                    time = dt(line.split()[0])
                except (ValueError, IndexError):
                    continue
                evaluation_file |= 'Evaluation' in line
                if run is None:
                    run = dict(log=path.name, start=time, scanner=False, first={},
                               processing={}, bell={}, plans={}, published={})
                if 'Scanner request:' in line:
                    run['scanner'] = scanner_file = True
                m = re.search(r'Processing ticker \(([^)]+)\), preset \(([^)]+)\)', line)
                if m:
                    run['first'].setdefault(m[1], time)
                    run['processing'].setdefault((m[1], m[2]), time)
                m = re.search(r'TodayResearchLike promotion applied: ([^.]+)\. Pattern=BellUp, Preset=([^,]+)', line)
                if m:
                    run['bell'][(m[1], m[2])] = time
                m = re.search(r'Trade plan BellUp phase-ready profile applied for ([^.]+)\.', line)
                if m:
                    run['plans'].setdefault(m[1], []).append(time)
                m = re.search(r'Candidate published: ([^.]+)\. PublishedAt=([^,\s]+)', line)
                if m:
                    run['published'][m[1]] = dt(m[2])
                if 'GetCandidates completed.' in line:
                    if run['scanner']:
                        run['end'] = time
                        runs.append(run)
                    run = None
        counts['scanner_log_files' if scanner_file else 'evaluation_only_log_files' if evaluation_file else 'other_log_files'] += 1
        if run and run['scanner']:
            counts['unfinished_scanner_runs'] += 1
    return runs


def describe(values):
    if not values:
        return None
    values = sorted(values)
    return dict(n=len(values), median=median(values), p90=values[int((len(values)-1)*.9)], max=values[-1])


def summarize(rows):
    usable = [r for r in rows if r.get('forecast') is not None]
    return dict(
        rows=len(rows), coverage=dict(Counter(r['coverage'] for r in rows)),
        first_processing_to_end_minutes=describe([r['first_to_end_seconds']/60 for r in rows]),
        selected_processing_to_end_minutes=describe([r['selected_to_end_seconds']/60 for r in rows]),
        green_body_forecasts=len(usable),
        forecast_mae_pct=mean(r['forecast_error_pct'] for r in usable) if usable else None,
        current_open_baseline_mae_pct=mean(r['baseline_error_pct'] for r in usable) if usable else None,
        forecast_beats_baseline=sum(r['forecast_error_pct'] < r['baseline_error_pct'] for r in usable),
        forecast_ties_baseline=sum(abs(r['forecast_error_pct']-r['baseline_error_pct']) < 1e-10 for r in usable),
        endpoint_above_open=sum(r['future_close'] > r['current_open'] for r in usable),
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', type=Path, default=Path(__file__).resolve().parents[2]/'Data')
    parser.add_argument('--since', default='2026-01-01')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    counts = Counter()
    runs = scan_runs(args.data/'logs', counts)
    counts['completed_scanner_runs'] = len(runs)
    grouped = defaultdict(list)
    seen = set()
    with (args.data/'Tickers/candidates.csv').open(encoding='utf-8-sig', newline='') as handle:
        for row in csv.DictReader(handle):
            if row.get('CandidateGroup') != 'Runaway' or row.get('CandidateSource') in ('Other','DiagnosticRejected'):
                continue
            scan = dt(row['ScanTime'])
            if scan.date().isoformat() < args.since:
                continue
            counts['admitted_runaway_rows'] += 1
            key = (row['Ticker'], row['PresetScanCode'])
            matches = [r for r in runs if r['start'] <= scan <= r['end'] and key in r['processing']]
            if len(matches) != 1:
                counts['ambiguous_log_match' if matches else 'missing_log_match'] += 1
                continue
            run = matches[0]
            if key not in run['bell'] and not row.get('PatternVerdictReason','').startswith('BellUp confirmed on '):
                counts['no_scan_time_bellup_evidence'] += 1
                continue
            identity = (run['log'], row['Ticker'], row['ScanTime'])
            if identity in seen:
                counts['duplicate_ticker_scan'] += 1
                continue
            seen.add(identity)
            published = run['published'].get(row['Ticker']) or dt(row.get('PublishedAt'))
            at = published or run['end']
            first = run['first'][row['Ticker']]
            selected = run['processing'][key]
            plans = run['plans'].get(row['Ticker'], [])
            grouped[row['Ticker']].append(dict(
                ticker=row['Ticker'], scan=row['ScanTime'], log=run['log'],
                preset=row['PresetScanCode'], first_processed=first.isoformat(),
                selected_processed=selected.isoformat(), scan_end=run['end'].isoformat(),
                reference_time=at.isoformat(), time_source='published' if published else 'completion_proxy',
                first_to_end_seconds=(run['end']-first).total_seconds(),
                selected_to_end_seconds=(run['end']-selected).total_seconds(),
                first_plan_to_end_seconds=(run['end']-min(plans)).total_seconds() if plans else None,
                saved_scan_price=row.get('ScanPrice'), saved_entry=row.get('EntryPrice'),
                bellup_evidence='log' if key in run['bell'] else 'candidate_snapshot',
            ))
    records = []
    for ticker, rows in sorted(grouped.items()):
        try:
            payload = json.loads((args.data/'cache'/f'{ticker}.json').read_text(encoding='utf-8-sig'))
            bars = {dt(b['Time']): b for b in payload.get('Timeframes',{}).get('M5',[])}
        except (OSError, ValueError):
            bars = {}
        for row in rows:
            at = dt(row['reference_time'])
            bucket = at.replace(minute=at.minute//5*5, second=0, microsecond=0)
            previous, current = bars.get(bucket-timedelta(minutes=5)), bars.get(bucket)
            row['m5_bucket'] = bucket.isoformat()
            row['coverage'] = 'missing_both' if not previous and not current else 'missing_previous' if not previous else 'missing_current' if not current else 'pair_available'
            if previous and current:
                op, close, current_open, future_close = (previous['Open'], previous['Close'], current['Open'], current['Close'])
                if min(op,close,current_open,future_close) <= 0:
                    row['coverage'] = 'invalid_price'
                else:
                    row.update(previous_open=op, previous_close=close, current_open=current_open,
                               previous_volume=previous.get('Volume',0), current_volume=current.get('Volume',0),
                               future_close=future_close, remaining_minutes=(bucket+timedelta(minutes=5)-at).total_seconds()/60)
                    if close > op:
                        forecast = current_open + close - op
                        row.update(forecast=forecast,
                            forecast_error_pct=100*abs(forecast-future_close)/current_open,
                            baseline_error_pct=100*abs(current_open-future_close)/current_open)
                    else:
                        row['coverage'] = 'nonpositive_body'
            records.append(row)
    latest = {}
    for row in records:
        key = (row['ticker'], row['scan'][:10])
        if key not in latest or row['scan'] > latest[key]['scan']:
            latest[key] = row
    daily = list(latest.values())
    report = dict(
        selection=dict(counts), joined_scans=len(set(r['log'] for r in records)),
        all_snapshots=summarize(records), latest_ticker_day=summarize(daily),
        latest_ticker_day_positive_volume=summarize([r for r in daily if r.get('previous_volume',0)>0 and r.get('current_volume',0)>0]),
        periods={p:summarize([r for r in daily if r['scan'][:7]==p]) for p in sorted(set(r['scan'][:7] for r in daily))},
        longest_detection_delays=sorted(records,key=lambda r:r['first_to_end_seconds'],reverse=True)[:5],
        mstr_september21=[r for r in records if r['ticker']=='MSTR' and r['scan'].startswith('2026-09-21')],
        limitations=[
            'First processing record is an observable timestamp, not exact broker first detection or price timestamp.',
            'Legacy completion is a conservative publication proxy; console output may precede it.',
            'Features use only previous completed M5 body and current opening price; completed current close is a future label.',
            'Current cached opening price assumes the bar had traded by the decision time; historical tick availability is unknown.',
            'Repeated tickers, market correlation, old code versions and cache revisions limit independence.',
            'This is exploratory forecast error to current M5 end, not an execution fill or profitability study.',
            'No fixed maximum latency is applied: each joined run uses its own measured completion time.',
        ])
    args.output.mkdir(parents=True,exist_ok=True)
    fields = list(dict.fromkeys(k for row in records for k in row))
    with (args.output/'replay.csv').open('w',newline='',encoding='utf-8') as handle:
        writer = csv.DictWriter(handle,fieldnames=fields)
        writer.writeheader()
        writer.writerows(records)
    (args.output/'summary.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
    print(json.dumps(report,indent=2,ensure_ascii=False))


if __name__ == '__main__':
    main()
