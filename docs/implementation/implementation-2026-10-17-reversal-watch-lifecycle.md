# Reversal-only watch cycles and meaningful Daily-mid drops

2026-10-17. User-directed lifecycle/universe change; no ranking weights,
pattern thresholds, confidence model or saved trading outcomes changed.

The former watch kept recognized names until expiry and accepted any tiny
below-Daily-mid price. WEAV exposed the problem: an August 18 rebound followed
by a long plateau, yet renewed in October around 0.18% below Daily mid.

The user explicitly set a minimum 5% below closed native Daily mid for entry
and re-entry. Watch cycles now retire on completed D1/H4 ReversalHook seeds.
A separate lifecycle ledger prevents repeated loser sightings in the same
consumed drop zone from immediately restoring them. A new excursion into the
deep zone can restart observation after recovery outside it; no prior-low
break is required. Watch-only contexts cannot produce BellUp. Live sources
retain their normal classification pipeline.

The [current lifecycle reference](../project-skills/ibswingtrader-scanner/references/REVERSAL_WATCH_LIFECYCLE.md)
documents exact state/price boundaries, cached-history migration, once-per-run
publication handling and persistence limitations. The scanner skill and
settings map were updated; dated earlier studies retain their original rules.

New source checks are in tests/ReversalWatchListChecks. Builds, tests,
application commands, broker requests and generated-data migration were not
run. Static checks and code review are distinct from executable validation.
