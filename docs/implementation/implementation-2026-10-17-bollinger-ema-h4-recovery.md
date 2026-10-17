# EMA Bollinger and fresh H4 support for recognized reversals

2026-10-17. User-requested behavior change after the VSTM investigation.

The shared Bollinger center becomes EMA20, seeded by the first SMA20, with
population sigma of the last20 closes retained. Upper/lower, width and
mid-distance metrics use the same center across scanner, watch replay and
feature dataset consumers. Saved rows remain historical; fresh rows carry
BollingerModel. Watch inspection cursors reset for the new model without
forgetting consumed cycles. Plain MA fields and MACD formulas are unchanged.

A recognized completed mid-bend episode can resume above mid/pre-bend close
when a fresh provisional current H4 histogram improves on the previous
closed H4, even if closed Daily momentum has not caught up. This is a
close-only computation from completed H4 history plus the accepted current
price. It makes no extra broker request and does not fabricate a forming
OHLC bar or establish a new seed. Native interval, history and freshness
checks apply; ordinary publication checks remain.

[Current full contract](../project-skills/ibswingtrader-scanner/references/BOLLINGER_EMA_AND_RECOVERY.md)
contains formula, provenance, historical-model limitations, VSTM reconstruction
and user-run checks. TWS exact indicator parity and broad Win/profit improvement
have not been established. In particular EMA changes VSTM seed geometry;
the original SMA label is not assumed to survive unchanged.

Source checks were prepared in tests/BollingerEmaChecks and
ReversalRecoveryChecks; watch-ledger and execution CSV checks were extended.
Application commands, builds, tests and broker requests were not run by Codex.
