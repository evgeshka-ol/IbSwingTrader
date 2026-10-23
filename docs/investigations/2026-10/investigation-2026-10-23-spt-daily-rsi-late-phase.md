# SPT: developing Daily BellUp rejected by RSI-rollover late-phase rule

Recorded2026-10-23. Sources: log-20261022_0300.log, candidates.csv,
evaluation-dataset.csv and Data/cache/SPT.json. Times America/New_York.
Read-only investigation; no application/broker/build/test run or production
policy/outcome change was made.

## Exact trigger

ScanTime2026-10-22 04:14:03, Other, zero plan, saved reason
BellUp confirmed on Daily; Late phase. The03:18:01 and03:34:09 log
decisions both explicitly say Daily RSI has already rolled over from a
recent peak. This is not an upper/lower-band completion verdict.

CandidateFinder.IsLateBellUpPhase for a selected Daily confirmation rejects
when the last-four Daily RSI maximum>=70 and latest RSI<previous RSI.
SPT saved tail73.68,77.56,80.04,77.99 meets it: a2.05-point decline from80.04.
No minimum decline, two-bar decline or Daily band deterioration is required
by this particular branch. RSI is the project's simple rolling14-change
calculation, so a lower reading can occur while price still closes higher.

Current source can override that Daily veto with fresh constructive H4
continuation, but SPT's saved H4 is cooling too: latest RSI75.11->69.80,
histogram0.10->0.08 (three-point RSI/macd tails also negative). This defeats
the freshness branch; other H4 terminal guards may also fail. A full
application replay was not run to identify the first override subcondition.

## Daily geometry at the saved boundary

Using the last two exported, rounded completed Daily points:

| Series | Previous | Latest |
|---|---:|---:|
| Mid | 10.96 | 11.13 |
| Upper | 12.62 | 13.01 |
| Lower | 9.30 | 9.25 |
| MACD histogram | 0.19 | 0.22 |
| Close series | 12.54 | 12.72 |

The Daily pattern still expands upward while lower remains directed down;
Daily MACD strengthens. The source's hard Late phase label therefore
overstates what is established about Daily geometry. H4 cooling is observable
risk/entry-timing context, not proof that the Daily BellUp is finished.
Native D1 October21 close is12.70 versus exported close-series12.72; the
local record/series difference is retained, not silently forced to parity.

The old RSI veto cites an August11 study with AUC0.61 on285 legacy Runaway
Win/Loss rows. That does not establish a reliable geometric exhaustion
boundary for every present Daily BellUp under the new EMA model. The dated
study itself should remain historical evidence, not be rewritten.

## Later move and pricing boundary

Saved evaluation amplitude16.42% is11.51 (October22 04:15) to13.40
(October22 16:15), min-first. Saved ScanPrice12.72 gives MaxPct5.35%.
NoEntry/zero plan means neither is an executed scanner profit.

The rejected row's reference was historical: publication04:14:04 retained
price-observed timestamp03:08:25, age3938.62seconds. The watch enrollment
check at04:13 had a fresh price11.51, but that quote did not replace the
rejected BellUp diagnostic's ScanPrice. A hypothetical relaxed veto would
still need normal fresh publication, structure and plan checks; it cannot
be claimed an automatic Win from the observed extrema alone.

## Suggested follow-up scope

Audit isolated Daily RSI rollovers with continuing Daily band/MACD expansion
as a separate cohort, preserving H4 cooling and actual fresh-entry checks.
Distinguish an RSI warning from a geometric late/completed phase. Compare
entered outcomes and timed entry opportunities, including known exhaustion
controls such as HELP/SECZ, before replacing a broad historical guard based
on this one favorable case. No change to that guard is included here.
