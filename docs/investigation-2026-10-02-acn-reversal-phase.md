# ACN reversal phase investigation — 2026-10-02

This is an investigation record, not a new production admission rule.

## Observed case

ACN, scan `2026-10-01 06:48:30`, was emitted as `Other` with
`ReversalHookPreparing (MACD-led) detected on D1`.

The scanner log used completed Daily data through September 30: close
183.37, middle Bollinger band 183.78. Lower-band deltas were
`-1.51, -0.82, -0.09`; MACD histogram was recovering and its line/signal
gap was narrowing. Price recovery started on September 29, while the
lower-band upward turn required by confirmed ReversalHook had not occurred.

The candidate CSV snapshot price was 183.52. The evaluation dataset's
`ScanPrice=196` is the first post-publication M5 open, not evidence that the
scanner had a fresh executable quote of 196. Its later recorded maximum
was 227.63 at October 1 10:10.

## Recognition gaps

- `Preparing` describes failed strict confirmation despite an established
  price/MACD recovery. It does not track the age or progress of a reversal.
- Lower-band confirmation can lag an impulsive price recovery. The user's
  primary reversal trigger is the middle band's bend after a clear decline;
  a mandatory upward lower-band hook can delay that trigger.
- For tickers with sufficient Daily data, H4 ReversalHook is not independently
  checked; H4 is currently a fallback when Daily pattern history is missing.
- Diagnostic candidates keep a snapshot price. The publication refresh is
  applied to playable BellUp candidates, so a diagnostic phase can remain
  based on a price that is already behind the market.

## Proposed investigation and implementation sequence

1. Replay only the data available at each scan. Compare middle-band prior
   decline and recent deceleration with the price/MACD recovery. Do not use
   later maxima as scanner features.
2. Add an independent H4 phase diagnosis with Daily context. Distinguish an
   emerging turn, an active recovery, and a recovery whose intended target
   has already been reached; recognition and trade readiness remain separate.
3. For an active recovery considered for promotion, obtain a fresh price and
   reassess progress against the saved bands before publishing a plan.
4. Preserve the confirmed rule while collecting the alternative diagnosis.
   Compare strong moves with failed rebounds before enabling admission or
   changing ranking weights.

The current dataset contains 50 Preparing rows (including repeated tickers
and scans) with incomplete future horizons. ACN and CTSH show strong upside,
while GLUE shows more than 21% downside. This is a usable investigation pool,
not evidence that every Preparing row should be promoted.
