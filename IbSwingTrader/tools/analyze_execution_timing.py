#!/usr/bin/env python3
"""Read-only M5 replay. No application/broker calls or dataset writes."""
import argparse
import csv
import json
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from pathlib import Path
from statistics import mean


def timestamp(value):
    return datetime.fromisoformat(value) if value else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', type=Path, default=Path(__file__).resolve().parents[2] / 'Data')
    parser.add_argument('--since', default='2026-09-01')
    args = parser.parse_args()
    latest = {}
    with (args.data / 'Tickers/candidates.csv').open(encoding='utf-8-sig', newline='') as handle:
        for row in csv.DictReader(handle):
            if row.get('CandidateGroup') != 'Runaway' or row.get('CandidateSource') in ('Other', 'DiagnosticRejected'):
                continue
            if not row.get('PatternVerdictReason', '').startswith('BellUp confirmed on '):
                continue
            scan = timestamp(row['ScanTime'])
            if scan.date().isoformat() < args.since:
                continue
            key = (row['Ticker'], scan.date())
            if key not in latest or scan > timestamp(latest[key]['ScanTime']):
                latest[key] = row

    by_ticker = defaultdict(list)
    for row in latest.values():
        by_ticker[row['Ticker']].append(row)
    counts = Counter(selected_ticker_days=len(latest))
    measurements = []
    missing = []
    for ticker, rows in sorted(by_ticker.items()):
        path = args.data / 'cache' / f'{ticker}.json'
        try:
            payload = json.loads(path.read_text(encoding='utf-8-sig'))
            bars = {timestamp(b['Time']): b for b in payload.get('Timeframes', {}).get('M5', [])}
        except (OSError, ValueError):
            bars = {}
        for row in rows:
            published = timestamp(row.get('PublishedAt')) or timestamp(row['ScanTime'])
            counts['exact_publication' if row.get('PublishedAt') else 'legacy_batch_time_proxy'] += 1
            bucket = published.replace(minute=published.minute // 5 * 5, second=0, microsecond=0)
            current = bars.get(bucket)
            previous = bars.get(bucket - timedelta(minutes=5))
            if not current or not previous:
                counts['missing_adjacent_m5'] += 1
                missing.append({'ticker': ticker, 'scan': row['ScanTime']})
                continue
            if min(current['Open'], current['Close'], previous['Open'], previous['Close']) <= 0:
                counts['invalid_price'] += 1
                continue
            body = previous['Close'] - previous['Open']
            if body <= 0:
                counts['nonpositive_previous_body'] += 1
                continue
            prediction = current['Open'] + body
            # Only the previous completed body and current OPEN are features.
            # The current completed CLOSE is a future label, never a feature.
            actual = current['Close']
            measurements.append({
                'ticker': ticker, 'scan': row['ScanTime'],
                'prediction': prediction, 'future_close': actual,
                'forecast_error_pct': 100 * abs(prediction - actual) / current['Open'],
                'open_baseline_error_pct': 100 * abs(current['Open'] - actual) / current['Open'],
            })
    report = {
        'counts': dict(counts),
        'positive_body_replays': len(measurements),
        'mean_absolute_error_pct': mean(x['forecast_error_pct'] for x in measurements) if measurements else None,
        'current_open_baseline_mae_pct': mean(x['open_baseline_error_pct'] for x in measurements) if measurements else None,
        'missing_examples': missing[:10],
        'limitations': [
            'Latest admitted BellUp snapshot per ticker/day; selected population, not an independent holdout.',
            'Legacy ScanTime approximates publication; it is not the historical quote timestamp.',
            'Cached forming-bar close is used only as the future five-minute endpoint label.',
            'Cache revisions and delayed first trades cannot reconstruct an exact historical live snapshot.',
            'These errors measure a five-minute forecast, not fills, profit or an optimal entry offset.',
        ],
    }
    print(json.dumps(report, indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()
