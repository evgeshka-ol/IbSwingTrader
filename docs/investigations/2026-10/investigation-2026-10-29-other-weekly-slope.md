# October26 Other: missed moves and a measurable gentle weekly mid

Date:2026-10-29. Status: exploratory saved-input/cache study; no live filter
change. User-selected case date, not an untouched holdout. Times are New York.

## Question and success criterion

The user highlighted NANO through GRAL in the October26 report: Other rows
with AmplitudePct>=10%, often recognized ReversalHook. Assess which gates
excluded them and whether a shallow weekly mid can provide useful context.
The user's description of SOFI's weekly mid is a **gentle decline**, not a
steep fall merely because its sign is negative.

Primary project success remains entered-plan Win/profit/confidence and the
rank1 minimum, not amplitude alone. Existing October26 rank1 rows are SGRY
(BellUp, entered Open, saved Win~98.3%) and AAL (ReversalHook, NoEntry).
The retrospective Other examples do not replace either original rank1 result.
No confidence calibration is established by unresolved SGRY or zero-plan Other.

## Inputs and predeclared primary comparison

- Exact saved candidate features, joined with evaluation by ticker/ScanTime/
  preset; current EMA model only, October17–26 inclusive. Available selected
  scan dates are19–23 and26:2,028 Other rows across763 tickers.
- Earlier comparison dates19–23 contain1,813 Other, including1,308 recognized
  ReversalHook. October26 contains215 Other, including130 recognized hooks.
- Weekly slope and a bounded1.5% candidate threshold describe the SOFI anchor;
  sensitivity0.5/1/1.5/2/3% is retained rather than selecting the best result.
  Additional momentum/support subgroups are exploratory follow-ups, not
  independently prevalidated rules.
- Source hashes and complete row evidence are in the linked artifacts.

## The fourteen highlighted rows

Recent slope is the geometric percent change per W1 observation across the
last three saved points. These are scan-time series, not final future weekly
values. A gentle-down candidate needs slope in[−1.5,0] and neither latest
transition falling more than1.5%. See the
[measurement reference](../../project-skills/ibswingtrader-scanner/references/WEEKLY_MID_SLOPE.md).

| Ticker | Saved amplitude | Main refusal | Recent W1 slope, %/transition | Gentle candidate |
| --- | ---: | --- | ---: | --- |
| NANO | 18.60% | Completed | −4.44 | No |
| USAR | 17.45% | Unconfirmed | −4.34 | No |
| WHR | 16.78% | Stalled | −2.55 | No |
| SOFI | 16.15% | Stalled | −1.23 | Yes |
| QURE | 15.47% | Unconfirmed | −3.21 | No |
| WYFI | 15.42% | Unconfirmed | −4.83 | No |
| CLYM | 15.21% | Stalled | −2.00 | No |
| INIO | 12.83% | W1 Neutral/Down | −3.65 | No |
| ARLO | 12.41% | Stalled | −0.38 | Yes |
| IBRX | 11.55% | Unconfirmed | −0.48 | Yes |
| GLXY | 11.41% | W1 Collapse/Down | −3.32 | No |
| SHAZ | 10.93% | H4 signal without required D1 support | −5.08 | No |
| THRM | 10.24% | W1 has no BellUp/early-recovery support | −1.66 | No |
| GRAL | 10.03% | Completed, including post-BellUp return to D1 mid | +4.64 | Rising |

Ten hooks were recognized: four Stalled, two Completed, three W1 support
failures and one D1 support failure. Four were explicitly **unconfirmed**;
the text `ReversalHook unconfirmed` is not recognition. Only SOFI and ARLO
among the ten recognized examples qualify as gently declining under this
working bound, and both stop at the phase gate before W1 support.
Thus changing W1 Down alone would not recover this highlighted block.

## Finding in the current classifier

BollingerFigureAnalyzer sets Direction from total percentage change across
the whole supplied series, rounded to two decimals. Flat requires rounded
zero; a gentle-decline state does not exist. It does not distinguish the
current tail from its long-history direction. THRM demonstrates the converse:
its entire saved mid series rises31.95→33.76, while the recent tail falls
34.91→34.31→33.76. Its original Up label and recent−1.66% slope can coexist.

This is a representational limitation. It does not establish that changing
the sign boundary alone produces profitable plans. Regime Collapse is also
derived from other band geometry and must not be globally redefined as gentle.

## Price-path proxy and denominator

Other has no saved playable plan. To test more than future maxima, use a
clearly separate proxy: buy at the first09:30 M5 **open** of the next full
regular session after publication (same day for a premarket scan), target+1.5%,
stop−3%, observe through16:00. Require all78 valid M5 bars and a positive-volume
opening bar. Prefer stop if stop and target both touch the same M5; a stop gap
uses the worse opening price. Retain sessions touching neither as Unresolved.
Stops1.5% and5% are sensitivity cases, not recommendations.

These Target/Stop results are **not saved-plan Wins/Losses or broker fills**.
The entry is next regular open, not the old ScanPrice or the new ReversalHook
publication policy. Premarket jumps are already paid at that entry. A Win~
estimate cannot be fitted or calibrated from this proxy without defining a
matching execution policy and validating its selection.

| Recognized Other population | Selected rows | Complete sessions | Target +1.5% | Stop −3% | Unresolved |
| --- | ---: | ---: | ---: | ---: | ---: |
| Earlier19–23, all | 1,308 | 444 | 209 | 124 | 111 |
| Earlier19–23, gentle candidate | 427 | 128 | 56 | 14 | 58 |
| October26, all | 130 | 130 | 94 | 20 | 16 |
| October26, gentle candidate | 42 | 42 | 33 | 1 | 8 |

Earlier gentle target share is56/128=43.75%, versus47.07% for all observed
recognized rows. Its stop share is10.94%, with45.31% Unresolved. This is more
consistent with calmer risk than a stand-alone winner predictor. Against
the remaining observed rows, a2,000-draw whole-ticker bootstrap gives a95%
target-share difference interval−16.21 to+6.19 percentage points, and a
stop-share difference interval−32.33 to−15.59 points. These describe the
observed coverage-selected sample, not causal or future performance.

October26 is broadly favorable: even the full recognized population reaches
the proxy target94/130 times. Its chosen high-amplitude examples cannot serve
as independent confirmation of a threshold. Repeated ticker plans on earlier
dates are dependent; whole-ticker resampling preserves those repetitions.

The high-amplitude block also contains adverse paths: NANO, WHR, INIO and
GLXY hit the proxy stop before target. WHR's next regular open is already
+11.23% above ScanPrice; SOFI's is+10.74%, ARLO's+9.99%. Their scan-relative
amplitudes are not the remaining profit after that opening. Some opportunities
may require an earlier or different entry, which this proxy does not evaluate.

## Two separate filter directions

**Bounded weekly support.** Within the gentle group, investigate D1
Active/MidCrossed DeepDeclineRecovery rejected only by W1 Down/NoSupport,
with at least2% room to W1 mid and improving closed D1, closed H4 and fresh
provisional H4 momentum. W1 Collapse, completed impulses, missing fresh price
and invalid plan remain outside this proposal. It differs from the October28
weekly-MACD exception and could operate on a Monday.

October26 selects DNLI, VSAT, AROC and QMCO; all four hit the proxy target
with each tested stop. Saved amplitudes3.65%,8.97%,6.07%,8.92% are below10%,
which does not make these useless for the minimum-profit objective.
The comparable earlier selection is only POR/BX/KBH/MBC on October22 evening;
none has complete next-session M5. There is therefore **no earlier complete
path validation for this bounded proposal**, and four favorable examples on
the selected case date do not validate a live relaxation.

**Stalled with improving D1 momentum.** Gentle W1 plus Stalled and improving
closed D1 histogram selects54 earlier rows:15 observed,9 Target/2 Stop/
4 Unresolved; the rest lack full coverage. On26 it selects SOFI, RKT, ELVN,
PENN, SMCI:4 Target/0 Stop/1 Unresolved. Applying exact phase-rejection and
at-least2% weekly-room restrictions leaves40 earlier rows, only8 observed:
3 Target/1 Stop/4 Unresolved, including repeated AMAT scans. ASML is a negative
example even with improving Daily MACD; the broader variant also loses CNH.
SOFI's own October19 gentle/Stalled case hits the proxy stop, although it
does not satisfy the improving-D1 addition. ARLO October26 fails that addition
because its D1 histogram has already declined from its latest peak.

This is a hypothesis for a distinct resumption/anticipatory-entry policy,
not permission to turn all Stalled into Active. Requiring fresh H4 momentum
again excludes SOFI26. Waiting for a new signal changes both entry price and
remaining room; promising the old14.43 after the07:00–07:05 boost would leak
future confirmation into the earlier entry.

## Decision and practical limit

Record a local percentage-per-W1 slope separately from cumulative direction,
and call the SOFI anchor gently declining. Treat1.5% per transition as a
working measurement boundary, not a profit-validated trading threshold.
Investigate bounded weekly support separately from Stalled recovery; do not
replace either with a blanket Down/Completed/Collapse bypass. The current
cache supports a risk-context hypothesis but does not yet validate admission
changes under the actual rebuilt TradeBuilder plans or the rank1 objective.
No live scanner/ranking/confidence change was made.

## Reproduction and artifacts

- [Offline script](../../../tools/analyze_other_weekly_slope.py).
- [Summary, case rows and source hashes](../../artifacts/analysis-2026-10-29-other-weekly-slope.json).
- [All2,028 selected rows, including negative controls and missing coverage](../../artifacts/analysis-2026-10-29-other-weekly-slope.csv.gz).

Only offline Python analysis was executed. Builds, tests, application commands,
broker requests and saved-outcome replay were not run. The old cache is
retrospective, mutable and incomplete; it does not prove timely historical
delivery. Coverage is especially poor on earlier dates864/1,308 recognized
rows lack a complete proxy session. Missing rows are not assigned losses.
Rounded saved weekly values and current-week partial observations add boundary
uncertainty. No invariant screen angle, guaranteed mean reversion, or improved
saved Win/profit/confidence has been established by this exploratory study.
