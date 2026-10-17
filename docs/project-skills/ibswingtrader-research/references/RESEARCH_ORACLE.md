# Research oracle: supporting recall evidence

Main artifact: Data/datasets/research_top_gainers.csv.

The [current success criteria](../../../guides/success-criteria.md) govern
project outcomes: entered-plan winners, their profit, and confidence
calibration. Research coverage and AmplitudePct are supporting diagnostics.
A first-ranked 1.5% Win can meet the minimum goal without being a top gainer.

## Correct benchmark

Compare yesterday's scanner with today's research winners to measure early
coverage. Do not use today's winners to justify today's earlier decision or
use later candles as scan-time features. Check snapshot availability and
horizons; cache timestamps alone do not prove timely broker delivery.

## Coverage classification

- Caught in playable summary: useful early coverage. Inspect the saved plan
  and evaluated outcome separately; a strong move need not be a played Win.
- Summary row absent from next-day movers: inspect its actual Win/profit.
  Absence alone does not mean failure under the current objective.
- Seen in logs, wishlist or Other but not playable: inspect promotion,
  recognition/readiness and fresh-price rejection reasons. A zero plan did
  not enter merely because the ticker later moved.
- Playable but below the leading row: inspect ranking against actual outcomes.
- Fully absent: inspect recall, universe and filtering.

Use saved BellUp, ReversalHook and Other groups separately. Legacy
Runaway/Reversal labels describe old datasets. Triangle remains diagnostic.

## Series-first diagnosis

Inspect saved real Bollinger upper/mid/lower curves, MACD line/signal/
histogram and RSI on Weekly/Daily/H4, with raw candles where needed.
Distinguish when the setup became observable from when price finally moved.
Use outcome labels only after forming features from the original snapshot.
