# Old versus formula entry accessibility

Read-only replay using scanner logs (`Scanner request`), cached M5 bars,
`candidates.csv` and `evaluation-dataset.csv`. Exits, stops, P&L and outcome
direction were ignored. The replay was rerun after the session restart.

The population contains 47 latest ticker/day snapshots with adjacent M5 bars,
a positive previous body, and a BellUp scanner/log join. For each snapshot,
the same future M5 bars were tested with:

- old: saved `EntryPrice`;
- formula: current M5 open plus the previous completed green M5 body;
- open baseline: current M5 open.

An entry is accessible when `Low <= entry <= High`, matching the evaluator's
touch rule. The 24-hour window is the closest comparison to the evaluator's
forward horizon; the one-hour window shows immediate accessibility.

| Saved evaluator outcome | Cases | Old touched (24h) | Formula touched (24h) | Rescued | Lost |
| --- | ---: | ---: | ---: | ---: | ---: |
| NoEntry | 12 | 0 | 8 | 8 | 0 |
| Open | 14 | 14 | 12 | 0 | 2 |

Restricting to the 11 `NoEntry` and `Open` rows with complete standard
extended-hours M5 coverage gives 7/11 formula touches among `NoEntry` and
10/11 among `Open`; old gives 0/11 and 11/11 respectively. The remaining
rows are not treated as proof of non-touch when the cache has gaps.

Across all 47 eligible cases, 24-hour accessibility is 25 old versus 35
formula entries. The paired result is 15 rescues and 5 losses, net +10.
At one hour it is 12 old versus 27 formula entries: 19 rescues and 4 losses,
net +15. The open-price baseline is higher still (40/47 at 24 hours and
35/47 at one hour), which means the formula remains a compromise between the
stale/deep old plan and an immediate current-price entry.

The result supports the stated purpose: the formula materially reduces
`NoEntry` caused by a deep old entry. It does not guarantee more final
`Open` rows because an existing `Open` row already has an old entry touched,
and the higher formula price can miss a later pullback. The formula's target
is entry-zone accessibility; do not judge it by wins or losses here.

The production rule applies only when a BellUp candidate is refreshed before
publication: use the current M5 open plus the previous completed green M5
body. If the adjacent pair is unavailable, use the fresh current M5 price as
a fallback. Reversal and other groups keep their existing entry logic.

Limitations: only 47 cases have the required cached pair; 21 of them have no
matching saved evaluation outcome; old CSV plans were generated before the
publication refresh; cached bars may have revisions; touching a candle is not
a fill guarantee; and no spread or order-book model is included.

Artifacts:

- `/tmp/ibswingtrader-entry-accessibility/entries.csv`
- `/tmp/ibswingtrader-entry-accessibility/summary.json`
- `tools/analyze_entry_accessibility.py`
