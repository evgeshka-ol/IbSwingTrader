# EPAM: morning coverage miss, evening Recent boost veto

Recorded 2026-10-21. Sources: log-20261019_0300.log,
log-20261019_1615.log, log-20260914_0812.log, candidates.csv,
evaluation-dataset.csv and Data/cache/EPAM.json. Times are America/New_York.
This is an investigation, not a scanner-policy change. Codex did not run
application/broker commands, builds, tests or a full historical replay.

## October19 morning: no classification took place

The morning log contains no EPAM occurrence: no history preparation,
pre-filter rejection, pattern decision or publication. The report likewise
has no EPAM row for ScanTime04:50:32. This is a coverage miss, not evidence
that the BellUp classifier saw and rejected the morning chart.

CandidateFinder processes active reversal-watch items and current TWS
scanner results; it does not automatically rescan all cached tickers.
TwsStockUniverseProvider requests ranked preset results with price5–300,
average volume>=1,000,000 and a requested RowsPerScan limit. The morning
performance summary records zero TOP_OPEN_PERC_GAIN rows and50
TOP_PERC_GAIN rows. No raw complete TWS universe snapshot was retained;
the exact reason EPAM was absent from the returned rankings is not logged.
Local pre-filter failures log the ticker, and none is present for EPAM.

The prior EPAM occurrence found in available logs is **September14**, not
October14: TOP_PERC_GAIN at08:52:52, followed by no confirmed H4/Daily BellUp
at08:59:32. The October19 history reload reports LatestDailyDate September14
versus expected October16. This corroborates lack of recent local history
refresh, not a continuous observation of EPAM between those dates.

The user bought at122 at07:00 October19 and reports130.68 on October21
premarket, about7.11% above that entry. This is user-supplied trade evidence;
it is not a saved scanner plan or a verified scanner Win. No claim is made
that an unperformed morning application replay would pass every gate.

## October19 evening: recognized, then rejected for entry timing

EPAM arrived through TOP_OPEN_PERC_GAIN, rank12, FirstSeenAt16:42:08.
History was refreshed at16:43. At17:04:54 the scanner recognized BellUp on
H4, then rejected timing:

- Last completed H4: October19 12:00.
- Green candle range4.38; preceding five-bar median range2.13.
- RangeRatio2.0563 versus configured threshold2.
- Saved reason: BellUp confirmed on H4; Recent boost.

The final17:28:37 row is Other with ScanPrice127.29 and zero plan prices.
"Confirmed" describes recognition; it does not mean admitted BellUp.
The continued move does not establish the correctness of an executable plan
that was never published. The veto's modest threshold excess is a reason
to audit continuation opportunities, not enough to retune the cutoff alone.

The saved signal timeframe is H4. Daily and Weekly directions were Up but
their regimes Pullback. This does not confirm the user's visual Daily or
emerging Weekly BellUp under the current classifier. Weekly remains context,
not a confirmed candidate-pattern timeframe.

Cached completed H4 contains the October16 after-hours16:00 bar with
O122.15/H122.19/L122.15/C122.19, and October19 08:00 and12:00 bars with
ranges4.86 and4.38. Those final Monday candles were not available at07:00;
they must not be used to prove morning admission or its timing veto.

## Recent admitted BellUp outcomes, as saved on October21

Repeated ticker/scan plans remain distinct. These are current snapshots,
not completed comparable-horizon cohorts. Only CandidateGroup=BellUp is
included; Other with a preserved BellUp diagnosis is excluded.

| ScanTime | BellUp rows | Win | Loss | Open | NoEntry |
|---|---:|---:|---:|---:|---:|
| October19 04:50:32 | 12 | 4 | 0 | 8 | 0 |
| October19 17:28:37 | 13 | 3 | 0 | 9 | 1 |
| October20 04:19:40 | 9 | 1 | 0 | 3 | 5 |
| October20 17:30:22 | 13 | 0 | 0 | 11 | 2 |

Totals47 rows:39 entered,8 Win,31 Open and8 NoEntry. Open is unresolved,
not a loss. The latest evening cohort has not yet had its next regular day.

Morning October19 winners: CVX1.50%, MUR3.50%, ACN2.78%, VET2.14%, with
saved CandidateDisplayRank6/10/11/12; first five are Open. Evening winners:
TTAN2.91%, CXM5.77%, SNAP3.66%, ranks9/10/12. Morning October20 winner:
TREX1.90%, rank4. Thus the concern about near-term winners not reaching
the top is concrete, but unresolved higher rows do not establish permanent
ranking failure or a final win rate.

For example morning rank1 ZTS planned entry79.62/exit84.19 (+5.74%),
while saved scan-relative maximum was only+1.51%. This motivates separate
target/horizon analysis; it does not justify relabeling it a Win or replacing
every target with1.5% using hindsight. The confidence model is experimental,
fitted on a small older cohort and not recalibrated for the new EMA model.

## Follow-up scope supported by this evidence

1. BellUp needs discovery coverage beyond current top movers if it is to
   catch gradual launches before they rank in a TWS preset. A separate
   liquid-universe/discovery policy and bounded continuation tracking should
   be specified; do not repurpose the reversal-only wishlist or assume
   short tracking alone would retain EPAM from September14 to October19.
2. Audit Recent boost rejections as continuation candidates, distinguishing
   first expansion, exhaustion and a later pause using native closed bars.
   Do not simply remove the veto or increase its threshold for EPAM.
3. Audit ranking and plan targets together on entered Win/Loss/Open,
   entry-relative profit and confidence calibration at matched horizons,
   with cache coverage checks before trusting extrema. Repeated ticker/date
   dependencies and unfinished cohorts must remain explicit.

No discovery pool, boost threshold, ranking weight, target or old outcome
was changed by this investigation.
