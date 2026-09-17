# Triangle: continuous post-spike plateau

Scanner correction, 2026-09-17.

Triangle remains a separate experimental pattern, not BellUp exhaustion.
The same detector is used for the scanner's H4 and Daily OHLC snapshots.

- Find the latest positive body at least twice the absolute previous body.
- Require at least two subsequent candles. Do not fall back to an older
  impulse if the latest one has insufficient follow-up or fails validation.
- Check every subsequent candle through the end of the supplied snapshot,
  not just the final three candles.
- Require small bodies, a narrow close range across the entire plateau,
  and closes near the impulse close on both sides (above and below).

Existing numerical thresholds are unchanged: maximum body is the larger of
25% of the impulse body and 0.3% of its closing price; maximum close spread
and distance from the impulse close use 50% and 0.5%, respectively.

This fixes disconnected historical-spike matches but does not establish
recognition accuracy. Tiny positive bodies after dojis still qualify under
the existing impulse rule and may reset the plateau anchor. Gap recognition,
Bollinger oval geometry, and offline evaluation parity are not added here.
The detector uses the supplied snapshot as-is, including a forming last bar
when present; it does not independently determine candle completion.

Regression checks: `tests/TrianglePatternChecks`. They cover continuous
plateaus, intervening bodies and excursions, plateaus far from the spike,
new impulses, insufficient history, and misaligned series. Build and test
execution remain the user's responsibility.
