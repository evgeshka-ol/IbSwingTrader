"""Fit the exploratory model from frozen offline replay artifacts; no app execution."""
import json,math,collections
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
r=json.load(open(ROOT / 'docs/analysis-2026-10-05-half-profit-targets.json'))
f=json.load(open(ROOT / 'docs/analysis-2026-10-05-win-features.json'))['samples'];ff={(x['ticker'],x['scan']):x['features'] for x in f}
a=[x for x in r if x['status']=='consistent' and x['new'] in ('Win','Loss')]
c=collections.Counter(x['ticker'] for x in a)
def features(x):
 s=ff[x['ticker'],x['scan']]
 return [min(x['new_profit'],20)/5,min(abs(float(x['stop'])/float(x['entry'])-1)*100,20)/5,min(max(s['DiagnosticsVolumeRatio20'],0),3)/.5,max(-30,min(30,s.get('H4RsiChange3',0)))/10]
w=[0.]*5
for _ in range(12000):
 g=[0.]*5
 for x in a:
  v=[1]+features(x);p=1/(1+math.exp(-max(-30,min(30,sum(t*b for t,b in zip(w,v))))));e=(p-(x['new']=='Win'))/c[x['ticker']]
  for j in range(5):g[j]+=e*v[j]
 # L2 shrinkage on slopes; symmetric prior on intercept.
 for j in range(1,5):g[j]+=4*w[j]
 g[0]+=2*(1/(1+math.exp(-w[0]))-.5)
 for j in range(5):w[j]-=.01*g[j]
print('N',len(a),'TICKERS',len(c),'COEF',w)
(ROOT / 'docs/model-2026-10-05-bellup-win.json').write_text(json.dumps(dict(n=len(a),tickers=len(c),coefficients=w)))
for x in r:
 if x['scan'].startswith('2026-10-02'):
  p=1/(1+math.exp(-sum(t*b for t,b in zip(w,[1]+features(x)))))
  print(x['ticker'],round(p*100,1))
