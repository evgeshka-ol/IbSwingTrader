# ReversalHook example collection

This note records chart-confirmed examples for the future `BellUp / ReversalHook / Other`
structure. A pattern is allowed to differ across timeframes: a daily ReversalHook can
coexist with an H4 Triangle or another continuation shape, especially during a reversal.

## VKTX — canonical Daily ReversalHook

- Source: TWS daily chart, September 2026.
- The completed daily candle on 2026-09-22 is the important confirmation: price turns up
  from below the Bollinger midline and crosses it with a strong green body and volume.
- The Yahoo chart omitted the latest daily candle; the TWS chart supplies the missing bar.
- The H4 chart is closer to a Triangle. This is compatible with, rather than a rejection
  of, the daily ReversalHook.
- Classification: `ReversalHook`, timeframe `D1`; H4 pattern should be recorded separately.

## Other confirmed examples

- BBNX — Daily ReversalHook.
- COIN — H4 ReversalHook transitioning into BellUp.
- DJT — Weekly ReversalHook.
- IBM — Weekly ReversalHook.

## Common observation

ReversalHook reversals often produce a strong impulse immediately after confirmation. The
impulse may occur in one candle or develop over several consecutive candles. Evaluation
should therefore retain both the confirmation timeframe and the first-impulse timing,
without requiring identical classifications on all timeframes.

## Data audit

The examples are present in the local candle cache and in `candidates.csv`, but their current labels are not a reliable ground-truth set:

- VKTX has D1/H4 data through 2026-09-22. The 2026-09-22 scanner log rejected the D1 hook because the pattern had already moved into the impulse (`LowerHooked=False`, `MidContextOk=False`, `BandCompression=False`). This is a timing miss, not evidence that the chart formation was absent.
- BBNX has D1/H4 data through 2026-09-18 and is present in candidates/evaluation, but the relevant rows are mostly classified as Other/BellUp or generic reversal diagnostics.
- COIN has complete D1/H4 data through 2026-09-22. Its H4 ReversalHook-to-BellUp example is not representable by the current D1-only ReversalHook check; scanner logs show D1 rejections while the H4 formation is a separate signal.
- DJT and IBM are present in cache/candidates, but the cached coverage ends before the chart examples' later dates (DJT 2026-09-10; IBM 2026-07-16), and no clean evaluation row exists for IBM.

The first criteria proposal is therefore a two-stage label: detect a hook on each timeframe independently, then record the subsequent impulse separately. A timeframe mismatch (for example, D1 ReversalHook + H4 Triangle) must not erase the positive D1 label.
