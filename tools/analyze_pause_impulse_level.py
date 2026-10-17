"""Prefix-only refinement: flat pause at impulse body top vs rising steps above it.

Reuses the frozen pause events; reads OHLC only to reconstruct body geometry.
Definitions are exploratory, not production BellUp/П labels or trade signals.
"""
import gzip
import json
import random
from collections import Counter, defaultdict
from statistics import mean, median

from analyze_pause_upper_position import ROOT, auc


def geometry(bars, peak, end):
    impulse = bars[peak]
    body = impulse['Close']-impulse['Open']
    if body <= 0:
        return None
    pause = bars[peak+1:end+1]
    if not pause:
        return None
    top = impulse['Close']
    prior_bodies = [abs(b['Close']-b['Open']) for b in bars[max(0,peak-5):peak]]
    prior_body_median = median(prior_bodies) if prior_bodies else 0
    gain = top-bars[peak-1]['Close'] if peak else 0
    centers = [(b['Open']+b['Close'])/2 for b in pause]
    short = max(abs(b['Close']-b['Open']) for b in pause)/body
    floor = (min(min(b['Open'],b['Close']) for b in pause)-top)/body
    progress = (centers[-1]-centers[0])/body
    spread = (max(centers)-min(centers))/body
    distance = max(abs(x-top) for x in centers)/body
    steps = (len(pause)>=2 and floor>=-1e-9 and progress>1e-9 and
             all(y>=x-1e-9 for x,y in zip(centers,centers[1:])) and short<=.4)
    flat = distance<=.25 and spread<=.25 and short<=.4
    return dict(body_floor_offset=floor, center_progress=progress,
        long_impulse=(gain>0 and body>=.5*gain and prior_body_median>0 and body>=2*prior_body_median),
        impulse_body_to_prior_median=body/prior_body_median if prior_body_median>0 else None,
        center_spread=spread, center_max_distance_to_top=distance,
        largest_pause_body_ratio=short,
        first_pause_open_equals_impulse_close=abs(pause[0]['Open']-top)<1e-8,
        shape='StepsAboveTop' if steps else 'FlatAtTop' if flat else 'Other',
        flat_tolerance={str(t):distance<=t and spread<=t and short<=.4 for t in (.1,.25,.5)},
        impulse_open=impulse['Open'], impulse_close=top,
        pause_open=[b['Open'] for b in pause], pause_close=[b['Close'] for b in pause])


def summarize(rows):
    if not rows:
        return {'n':0}
    return dict(n=len(rows), tickers=len({r['ticker'] for r in rows}),
        red_pct=100*mean(r['next_candle_pct'] < -1e-9 for r in rows),
        down5_pct=100*mean(r['next_candle_pct'] <= -5 for r in rows),
        up5_pct=100*mean(r['next_candle_pct'] >= 5 for r in rows),
        median_next_candle_pct=median(r['next_candle_pct'] for r in rows),
        median_rise_pct=median(r['rise_pct'] for r in rows))


def bootstrap(rows):
    counts=defaultdict(lambda:[0,0,0,0])
    for r in rows:
        if r['shape'] not in ('FlatAtTop','StepsAboveTop'):
            continue
        k=0 if r['shape']=='FlatAtTop' else 2
        counts[r['ticker']][k]+=r['next_candle_pct']<=-5
        counts[r['ticker']][k+1]+=1
    entries=list(counts.values())
    totals=[sum(x[i] for x in entries) for i in range(4)]
    if not totals[1] or not totals[3]:
        return None
    rng=random.Random(20261016)
    differences=[]
    for _ in range(400):
        sample=rng.choices(entries,k=len(entries))
        s=[sum(x[i] for x in sample) for i in range(4)]
        if s[1] and s[3]:
            differences.append(100*(s[0]/s[1]-s[2]/s[3]))
    differences.sort()
    return dict(flat_minus_steps_down5_pp=100*(totals[0]/totals[1]-totals[2]/totals[3]),
        ticker_bootstrap_95pct=[differences[int(.025*len(differences))],differences[int(.975*len(differences))]])


def main():
    source=ROOT/'docs/analysis-2026-10-15-pause-upper-position.events.json.gz'
    with gzip.open(source,'rt') as f:
        events=json.load(f)
    selected=[r for r in events if r['rise_type']=='Impulse1' and r['age'] in (2,3,4) and '1' in r['outcomes']]
    by_ticker=defaultdict(list)
    for r in selected:
        by_ticker[r['ticker']].append(r)
    observations=[]
    excluded=Counter()
    for index,(ticker,rows) in enumerate(sorted(by_ticker.items())):
        if index%400==0:
            print(f'Tickers processed: {index}',flush=True)
        path=ROOT/'Data/cache'/f'{ticker}.json'
        if not path.exists():
            excluded['missing_cache']+=len(rows)
            continue
        cache=json.loads(path.read_text(encoding='utf-8-sig')).get('Timeframes',{})
        frames={}
        for tf in ('H4','D1'):
            bars=sorted({b['Time']:b for b in cache.get(tf,[])}.values(),key=lambda b:b['Time'])
            frames[tf]=(bars,{b['Time']:i for i,b in enumerate(bars)})
        for r in rows:
            bars,indices=frames[r['timeframe']]
            peak,end=indices.get(r['ramp_end']),indices.get(r['time'])
            if peak is None or end is None or end-peak!=r['age']:
                excluded['missing_or_changed_interval']+=1
                continue
            if any(abs(bars[end][k.title()]-r[k])>1e-6 for k in ('low','high','close')):
                excluded['signal_ohlc_changed']+=1
                continue
            g=geometry(bars,peak,end)
            if g is None:
                excluded['no_positive_body']+=1
                continue
            outcome=r['outcomes']['1']
            observations.append(dict(ticker=ticker,time=r['time'],timeframe=r['timeframe'],age=r['age'],
                rise_pct=r['rise_pct'],position=r['position'],group=r['group'],upper=r['upper'],
                pause_positive_volume_fraction=r['positive_volume_fraction'],
                next_candle_pct=100*((100+outcome['close_pct'])/(100+outcome['next_open_pct'])-1),
                next_close_pct=outcome['close_pct'],next_low_pct=outcome['min_pct'],
                **g))
    summaries=[]
    for tf in ('H4','D1'):
        for age in (2,3,4):
            broad=[r for r in observations if r['timeframe']==tf and r['age']==age]
            base=[r for r in broad if r['long_impulse']]
            groups={g:summarize([r for r in base if r['shape']==g]) for g in ('FlatAtTop','StepsAboveTop','Other')}
            body_groups={'BelowBody':{'BelowLow','LowerWick'},'InsideBody':{'Body'},'AboveBody':{'UpperWick','AboveHigh'}}
            summaries.append(dict(timeframe=tf,age=age,all=summarize(base),groups=groups,
                broad_sensitivity={g:summarize([r for r in broad if r['shape']==g]) for g in groups},
                band_interaction={g:{p:summarize([r for r in base if r['shape']==g and r['position'] in positions])
                    for p,positions in body_groups.items()} for g in groups},
                chronological={period:{g:summarize([r for r in base if r['shape']==g and
                    (r['time']<'2026-09-01' if period=='before_sep' else r['time']>='2026-09-01')]) for g in groups}
                    for period in ('before_sep','sep_onward')},
                continuous_auc={k:auc([(r[k],r['next_candle_pct']<=-5) for r in base])
                    for k in ('body_floor_offset','center_progress','center_spread','center_max_distance_to_top')},
                flat_tolerance_sensitivity={str(t):summarize([r for r in base if r['flat_tolerance'][str(t)] and
                    r['shape']!='StepsAboveTop']) for t in (.1,.25,.5)},
                price5_positive_volume={g:summarize([r for r in base if r['shape']==g and r['impulse_close']>=5 and
                    r['pause_positive_volume_fraction']==1]) for g in groups},
                rise_size_strata={f'{lo}-{hi}':{g:summarize([r for r in base if r['shape']==g and lo<=r['rise_pct']<hi])
                    for g in groups} for lo,hi in ((5,10),(10,20),(20,100000))},
                first_pause_open_equals_impulse_close_pct=100*mean(r['first_pause_open_equals_impulse_close'] for r in base) if base else None,
                bootstrap=bootstrap(base)))
    output=ROOT/'docs/analysis-2026-10-16-pause-impulse-level.json'
    result=dict(source=str(source.relative_to(ROOT)),matched=len(observations),excluded=dict(excluded),summaries=summaries,
        examples=[r for r in observations if r['ticker']=='NVAX' and r['time']=='2026-10-06T04:00:00'])
    output.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n')
    with gzip.open(output.with_suffix('.events.json.gz'),'wt') as f:
        json.dump(observations,f,separators=(',',':'))
    print(json.dumps({'matched':len(observations),'excluded':dict(excluded)}),flush=True)
    for s in summaries:
        print(s['timeframe'],s['age'],json.dumps({'groups':s['groups'],'bootstrap':s['bootstrap'],
            'auc':s['continuous_auc'],'first_open_equals_prior_close_pct':s['first_pause_open_equals_impulse_close_pct']}),flush=True)


if __name__=='__main__':
    main()
