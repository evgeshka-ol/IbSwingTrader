"""Re-summarize saved pause observations by upper-band position versus candle body.

Does not test paired H4/D1 gating, mirror patterns, or complete Triangle geometry.
"""
import gzip
import json

from analyze_pause_upper_position import ROOT, AGES, HORIZONS, summarize, cluster_difference


def main():
    source = ROOT / 'docs/artifacts/analysis-2026-10-15-pause-upper-position.events.json.gz'
    with gzip.open(source, 'rt') as f:
        events = json.load(f)
    groups = {'BelowBody': {'BelowLow', 'LowerWick'},
              'InsideBody': {'Body'}, 'AboveBody': {'UpperWick', 'AboveHigh'}}
    summaries = []
    for tf in ('H4', 'D1'):
        for rise in ('Impulse1', 'Rise3or6'):
            for age in AGES:
                base = [r for r in events if r['timeframe']==tf and r['rise_type']==rise and r['age']==age]
                summaries.append(dict(timeframe=tf, rise_type=rise, age=age,
                    groups={name:{str(h):summarize([r for r in base if r['position'] in positions], h)
                                  for h in HORIZONS} for name,positions in groups.items()}))
    primary = [r for r in events if r['timeframe']=='H4' and r['rise_type']=='Impulse1' and r['age']==2]
    comparison = [dict(r, group='BelowLow' if r['position'] in groups['BelowBody'] else 'InsideRange')
                  for r in primary if r['position'] in groups['BelowBody'] | groups['InsideBody']]
    examples = sorted([r for r in primary if r['position']=='Body' and
        r['time']>='2026-09-01' and r['close']>=5 and r['outcomes'].get('3',{}).get('label')=='Down'],
        key=lambda r:r['time'], reverse=True)[:5]
    result = dict(source=str(source.relative_to(ROOT)), scope='Upper position vs body, independent H4/D1 cohorts',
                  body_rule='Upper < min(Open,Close); positions retained by the original study',
                  summaries=summaries, primary_below_vs_inside_difference=cluster_difference(comparison),
                  examples=examples)
    # The immediate next H4 is the forming/current candle at the decision time.
    # Separate close-to-close changes from the candle's own open-to-close move.
    result['immediate_h4'] = {}
    for name, positions in groups.items():
        selected = [r for r in primary if r['position'] in positions and '1' in r['outcomes']]
        outcomes = [r['outcomes']['1'] for r in selected]
        candle_returns = [100*((100+o['close_pct'])/(100+o['next_open_pct'])-1) for o in outcomes]
        n = len(outcomes)
        result['immediate_h4'][name] = dict(n=n,
            close_to_prior_close_down_5_n=sum(o['close_pct']<=-5 for o in outcomes),
            low_to_prior_close_down_5_n=sum(o['min_pct']<=-5 for o in outcomes),
            red_candle_n=sum(v < -1e-9 for v in candle_returns),
            open_to_close_down_5_n=sum(v<=-5 for v in candle_returns))
    out = ROOT / 'docs/artifacts/analysis-2026-10-16-pause-body-position.json'
    out.write_text(json.dumps(result, indent=2, ensure_ascii=False)+'\n')
    selected = next(s for s in summaries if s['timeframe']=='H4' and s['rise_type']=='Impulse1' and s['age']==2)
    print(json.dumps({name:{k:v for k,v in values['3'].items() if k in ('n','down_pct','up_pct')}
                      for name,values in selected['groups'].items()},indent=2))
    print(json.dumps(result['primary_below_vs_inside_difference'], indent=2))


if __name__ == '__main__':
    main()
